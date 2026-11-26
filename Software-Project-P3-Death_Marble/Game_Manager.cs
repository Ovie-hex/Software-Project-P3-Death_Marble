using System;
using System.Collections.Generic;
using System.Linq;

namespace Software_Project_P3_Death_Marble
{
    /// <summary>
    /// Owns game state and rules. MainGame communicates with this module only
    /// through the Connector game bus.
    /// </summary>
    internal sealed class Game_Manager : IDisposable
    {
        private const int MaxRound = 40;
        private const int SuddenDeathRound = 20;

        private readonly Random random = new();
        private readonly Connector.BindableEvent connector;
        private readonly Dictionary<int, int> prisonTurns = new();
        private List<int> turnOrder = new();
        private PendingCardTarget? pendingCardTarget;
        private int? pendingTeleportPlayer;
        private int pendingTeleportSourceTile = -1;
        private bool disposed;

        private sealed record PendingCardTarget(
            int OwnerId,
            CardData Card,
            int CardIndex,
            bool IsImmediateCard,
            bool EndsTurnAfterResolution);

        public List<Player> Players { get; private set; } = new();
        public StageData? CurrentStage { get; private set; }
        public int CurrentPlayerIndex { get; private set; }
        public int CurrentRound { get; private set; } = 1;
        public int RemainingMoves { get; private set; }
        public bool IsGameRunning { get; private set; }
        public bool IsMoving { get; private set; }
        public bool HasRolledThisTurn { get; private set; }
        public bool IsWaitingForPath { get; private set; }
        public bool IsWaitingForTeleport => pendingTeleportPlayer.HasValue;
        public bool IsWaitingForCardTarget => pendingCardTarget != null;
        public Player CurrentPlayer => Players.Single(player => player.Id == CurrentPlayerId);
        public int CurrentPlayerId => turnOrder.Count == 0 ? -1 : turnOrder[CurrentPlayerIndex];

        public Game_Manager()
        {
            connector = Connector.GetBindableEvent(GameConnectorNames.Bus);
            connector.Name = GameConnectorNames.GameManager;
            connector.OnInvoke = (_, payload) =>
            {
                if (payload is GameCommand command)
                    HandleCommand(command);
            };
        }

        private void HandleCommand(GameCommand command)
        {
            try
            {
                switch (command.Type)
                {
                    case GameCommandType.Initialize:
                        InitializeGame(command.Stage, command.TurnOrder);
                        break;
                    case GameCommandType.RollDice:
                        RollDice(command.PreferredDiceNumber);
                        break;
                    case GameCommandType.AdvanceMovement:
                        AdvanceMovement();
                        break;
                    case GameCommandType.SelectPath:
                        SelectPath(command.Index);
                        break;
                    case GameCommandType.SelectTeleportDestination:
                        SelectTeleportDestination(command.TileIndex);
                        break;
                    case GameCommandType.UseCard:
                        UseCard(command.Index);
                        break;
                    case GameCommandType.SelectCardTarget:
                        SelectCardTarget(command.PlayerId);
                        break;
                    case GameCommandType.CancelCardTarget:
                        CancelCardTarget();
                        break;
                }
            }
            catch (Exception exception)
            {
                Publish(GameMessageType.Error, text: exception.Message);
            }
        }

        private void InitializeGame(StageData? stage, IReadOnlyList<int> requestedOrder)
        {
            if (stage == null)
                throw new ArgumentNullException(nameof(stage));
            if (requestedOrder == null || requestedOrder.Count is < 2 or > 4 ||
                requestedOrder.Distinct().Count() != requestedOrder.Count ||
                requestedOrder.Any(id => id < 1 || id > requestedOrder.Count))
            {
                throw new ArgumentException("플레이어 턴 순서가 올바르지 않습니다.");
            }
            if (stage.Map.Start == null)
                throw new InvalidOperationException("스테이지에 시작 타일이 없습니다.");

            if (stage.Map.Tiles.Count == 0)
                stage.Map.Tiles.Add(stage.Map.Start);
            if (!stage.Map.Tiles.Any(tile => ReferenceEquals(tile, stage.Map.Start)))
                stage.Map.Tiles.Insert(0, stage.Map.Start);

            CurrentStage = stage;
            turnOrder = requestedOrder.ToList();
            Players = Enumerable.Range(1, turnOrder.Count)
                .Select(id => new Player(id, $"PLAYER {id}", stage.Map.Start!))
                .ToList();
            prisonTurns.Clear();
            foreach (int id in turnOrder)
                prisonTurns[id] = 0;

            CurrentPlayerIndex = 0;
            CurrentRound = 1;
            RemainingMoves = 0;
            IsGameRunning = true;
            IsMoving = false;
            HasRolledThisTurn = false;
            IsWaitingForPath = false;
            pendingTeleportPlayer = null;
            pendingTeleportSourceTile = -1;
            pendingCardTarget = null;

            Log("게임이 초기화되었습니다.");
            Log("턴 순서: " + string.Join(" → ", turnOrder.Select(id => $"P{id}")));
            Log($"{CurrentPlayer.Name}의 턴입니다.");
            Publish(GameMessageType.StateChanged);
        }

        private void RollDice(int? preferredNumber)
        {
            if (!IsGameRunning || IsMoving || HasRolledThisTurn ||
                IsWaitingForPath || IsWaitingForTeleport || IsWaitingForCardTarget ||
                CurrentPlayer.IsEliminated)
            {
                return;
            }

            int result = preferredNumber is int preferred
                ? DiceProbability.Roll(random, preferred, CurrentPlayer.HP, CurrentPlayer.MaxHP)
                : random.Next(1, 7);

            HasRolledThisTurn = true;
            RemainingMoves = result;
            IsMoving = true;
            Log($"{CurrentPlayer.Name} 주사위 결과: {result}");
            Publish(GameMessageType.DiceRolled, playerId: CurrentPlayer.Id, value: result);
        }

        private void AdvanceMovement()
        {
            if (!IsGameRunning || !IsMoving || IsWaitingForPath ||
                IsWaitingForTeleport || IsWaitingForCardTarget)
            {
                return;
            }

            if (RemainingMoves <= 0)
            {
                FinishMovement();
                return;
            }

            TileData? currentTile = CurrentPlayer.CurrentTile;
            if (currentTile == null)
            {
                RemainingMoves = 0;
                FinishMovement();
                return;
            }

            if (currentTile.Next.Count > 1)
            {
                IsWaitingForPath = true;
                Log("갈림길에 도착했습니다.");
                Publish(
                    GameMessageType.PathSelectionRequired,
                    playerId: CurrentPlayer.Id,
                    optionIndices: currentTile.Next.Select(GetTileIndex).ToList());
                return;
            }

            if (currentTile.Next.Count == 0)
            {
                Log("더 이상 이동할 수 없습니다.");
                RemainingMoves = 0;
                FinishMovement();
                return;
            }

            MoveToTile(CurrentPlayer, currentTile.Next[0]);
        }

        private void SelectPath(int pathIndex)
        {
            if (!IsGameRunning || !IsWaitingForPath || CurrentPlayer.CurrentTile == null)
                return;

            List<TileData> choices = CurrentPlayer.CurrentTile.Next;
            if (pathIndex < 0 || pathIndex >= choices.Count)
                throw new ArgumentOutOfRangeException(nameof(pathIndex));

            IsWaitingForPath = false;
            Log($"{CurrentPlayer.Name}이(가) {pathIndex + 1}번 경로를 선택했습니다.");
            MoveToTile(CurrentPlayer, choices[pathIndex]);
        }

        private void MoveToTile(Player player, TileData tile)
        {
            player.CurrentTile = tile;
            RemainingMoves = Math.Max(0, RemainingMoves - 1);
            Publish(
                GameMessageType.PlayerMoved,
                playerId: player.Id,
                value: GetTileIndex(tile));
        }

        private void FinishMovement()
        {
            if (!IsMoving || RemainingMoves > 0)
                return;

            IsMoving = false;
            Publish(GameMessageType.MovementFinished, playerId: CurrentPlayer.Id);
            if (!IsGameRunning)
                return;

            TileData? tile = CurrentPlayer.CurrentTile;
            if (tile == null)
            {
                EndTurn();
                return;
            }

            string tileLabel = GetTileLabel(tile);
            Log(string.IsNullOrWhiteSpace(tileLabel)
                ? $"{CurrentPlayer.Name}: {GetTileIndex(tile)}번 타일 도착"
                : $"{CurrentPlayer.Name}: {tileLabel} 타일 도착");
            ResolveTileArrival(CurrentPlayer, tile);
        }

        private void ResolveTileArrival(Player player, TileData tile)
        {
            bool requiresTeleportChoice = false;

            if (!ReferenceEquals(tile, CurrentStage?.Map.Start))
            {
                switch (tile.Type)
                {
                    case TileType.Damage:
                        ChangeHP(player, -tile.Amount);
                        break;
                    case TileType.Heal:
                        ChangeHP(player, tile.Amount);
                        break;
                    case TileType.Card:
                        ReceiveCard(player);
                        break;
                    case TileType.Prison:
                        prisonTurns[player.Id] = Math.Max(prisonTurns[player.Id], 1);
                        Log($"{player.Name}이(가) 감옥에 도착해 다음 차례를 쉽니다.");
                        break;
                    case TileType.Hole:
                        MoveToRandomOtherHole(player, tile);
                        break;
                    case TileType.Teleport:
                        requiresTeleportChoice = BeginTeleportSelection(player, tile);
                        break;
                }
            }

            if (requiresTeleportChoice)
                return;

            ApplyArrivalDamage(player);
            if (CheckGameOver())
                return;
            if (pendingCardTarget != null)
            {
                Publish(GameMessageType.StateChanged);
                return;
            }

            EndTurn();
        }

        private void ReceiveCard(Player player)
        {
            CardData? card = DrawRandomCard();
            if (card == null)
            {
                Log("이 스테이지에는 획득할 카드가 없습니다.");
                return;
            }

            if (card.Usable)
            {
                if (!player.AddCard(card))
                    Log($"{player.Name}: 액티브 카드 보유 한도 3장 — {card.Name} 무효");
                else
                    Log($"{player.Name}: 액티브 카드 획득 — {card.Name}");
                return;
            }

            Log($"{player.Name}: 즉발 카드 발동 — {card.Name}");
            BeginOrResolveCard(player, card, -1, isImmediateCard: true, endsTurn: true);
        }

        private bool BeginTeleportSelection(Player player, TileData sourceTile)
        {
            if (CurrentStage == null || CurrentStage.Map.Tiles.Count <= 1)
            {
                Log($"{player.Name}: 이동할 다른 타일이 없습니다.");
                return false;
            }

            pendingTeleportPlayer = player.Id;
            pendingTeleportSourceTile = GetTileIndex(sourceTile);
            Log($"{player.Name}: 텔레포트 목적지를 선택하세요.");
            Publish(
                GameMessageType.TeleportSelectionRequired,
                playerId: player.Id,
                value: pendingTeleportSourceTile);
            return true;
        }

        private void SelectTeleportDestination(int destinationIndex)
        {
            if (!IsWaitingForTeleport || CurrentStage == null)
                return;
            if (destinationIndex < 0 || destinationIndex >= CurrentStage.Map.Tiles.Count ||
                destinationIndex == pendingTeleportSourceTile)
            {
                return;
            }

            Player player = Players.Single(item => item.Id == pendingTeleportPlayer);
            TileData destination = CurrentStage.Map.Tiles[destinationIndex];
            pendingTeleportPlayer = null;
            pendingTeleportSourceTile = -1;
            player.CurrentTile = destination;
            Publish(GameMessageType.PlayerMoved, playerId: player.Id, value: destinationIndex);
            string destinationLabel = GetTileLabel(destination);
            Log(string.IsNullOrWhiteSpace(destinationLabel)
                ? $"{player.Name}: 텔레포트 — {destinationIndex}번 타일"
                : $"{player.Name}: 텔레포트 — {destinationLabel} 타일 {destinationIndex}");
            ResolveTileArrival(player, destination);
        }

        private void MoveToRandomOtherHole(Player player, TileData sourceHole)
        {
            if (CurrentStage == null)
                return;

            int sourceIndex = GetTileIndex(sourceHole);
            List<TileData> holes = CurrentStage.Map.Tiles
                .Where((tile, index) => index != sourceIndex && tile.Type == TileType.Hole)
                .ToList();
            if (holes.Count == 0)
            {
                Log($"{player.Name}: 이동할 다른 Hole 타일이 없습니다.");
                return;
            }

            TileData destination = holes[random.Next(holes.Count)];
            player.CurrentTile = destination;
            Publish(GameMessageType.PlayerMoved, playerId: player.Id, value: GetTileIndex(destination));
            Log($"{player.Name}: 다른 Hole 타일 {GetTileIndex(destination)}로 이동했습니다.");
        }

        private void ApplyArrivalDamage(Player player)
        {
            if (CurrentRound >= SuddenDeathRound && !player.IsEliminated)
            {
                ChangeHP(player, -5);
                Log($"{player.Name}: 서든 데스 HP -5");
            }
        }

        private void UseCard(int cardIndex)
        {
            if (!IsGameRunning || IsMoving || HasRolledThisTurn ||
                IsWaitingForPath || IsWaitingForTeleport || IsWaitingForCardTarget)
            {
                return;
            }

            if (cardIndex < 0 || cardIndex >= CurrentPlayer.Cards.Count)
                return;

            CardData card = CurrentPlayer.Cards[cardIndex];
            if (!card.Usable)
                return;

            BeginOrResolveCard(CurrentPlayer, card, cardIndex, isImmediateCard: false, endsTurn: false);
        }

        private void BeginOrResolveCard(
            Player owner,
            CardData card,
            int cardIndex,
            bool isImmediateCard,
            bool endsTurn)
        {
            bool needsTarget = (card.Effects ?? new List<EffectData>()).Any(effect =>
                effect.Type != EffectType.None && effect.Target == TargetType.Other);
            if (needsTarget)
            {
                List<int> targets = turnOrder
                    .Where(id => id != owner.Id && !Players.Single(player => player.Id == id).IsEliminated)
                    .ToList();
                if (targets.Count > 0)
                {
                    pendingCardTarget = new PendingCardTarget(
                        owner.Id,
                        card,
                        cardIndex,
                        isImmediateCard,
                        endsTurn);
                    Publish(
                        GameMessageType.CardTargetSelectionRequired,
                        text: card.Name,
                        playerId: owner.Id,
                        cardIndex: cardIndex,
                        isImmediateCard: isImmediateCard,
                        optionIndices: targets);
                    return;
                }
                Log($"{owner.Name}: 선택 가능한 대상이 없습니다.");
            }

            ResolveCardEffects(owner, card, selectedOther: null);
            if (!isImmediateCard)
                owner.RemoveCard(card);
            Log($"{owner.Name}: {card.Name} 사용");
            if (CheckGameOver())
                return;
            if (!endsTurn && owner.IsEliminated)
                EndTurn();
            else
                Publish(GameMessageType.StateChanged);
        }

        private void SelectCardTarget(int targetId)
        {
            if (pendingCardTarget == null)
                return;

            PendingCardTarget pending = pendingCardTarget;
            Player? target = Players.FirstOrDefault(player => player.Id == targetId);
            if (target == null || target.IsEliminated || target.Id == pending.OwnerId)
                return;

            pendingCardTarget = null;
            Player owner = Players.Single(player => player.Id == pending.OwnerId);
            ResolveCardEffects(owner, pending.Card, target.Id);
            if (!pending.IsImmediateCard)
                owner.RemoveCard(pending.Card);
            Log($"{owner.Name}: {pending.Card.Name} 사용 · {target.Name} 선택");

            if (CheckGameOver())
                return;
            if (pending.EndsTurnAfterResolution || owner.IsEliminated)
                EndTurn();
            else
                Publish(GameMessageType.StateChanged);
        }

        private void CancelCardTarget()
        {
            if (pendingCardTarget == null || pendingCardTarget.IsImmediateCard)
                return;

            pendingCardTarget = null;
            Publish(GameMessageType.StateChanged);
        }

        private void ResolveCardEffects(Player owner, CardData card, int? selectedOther)
        {
            foreach (EffectData effect in card.Effects ?? new List<EffectData>())
            {
                if (effect.Type == EffectType.None)
                    continue;

                int repeat = Math.Clamp(effect.Count, 0, 50);
                for (int i = 0; i < repeat; i++)
                {
                    if (effect.Type == EffectType.Swap && effect.Target == TargetType.All)
                    {
                        ShuffleAlivePlayerPositions();
                        continue;
                    }

                    foreach (Player target in ResolveCardTargets(owner, effect.Target, selectedOther))
                    {
                        switch (effect.Type)
                        {
                            case EffectType.Damage:
                                ChangeHP(target, -effect.Amount);
                                break;
                            case EffectType.Heal:
                                ChangeHP(target, effect.Amount);
                                break;
                            case EffectType.Teleport:
                                RelocatePlayerToRandomTile(target);
                                break;
                            case EffectType.Swap:
                                if (target.Id != owner.Id)
                                    SwapPlayerPositions(owner, target);
                                break;
                        }
                    }
                }
            }
        }

        private List<Player> ResolveCardTargets(Player owner, TargetType target, int? selectedOther)
        {
            return target switch
            {
                TargetType.Self => owner.IsEliminated ? new() : new() { owner },
                TargetType.Other => selectedOther is int id
                    ? Players.Where(player => player.Id == id && !player.IsEliminated).ToList()
                    : new(),
                TargetType.All => Players.Where(player => !player.IsEliminated).ToList(),
                TargetType.Random => Players
                    .Where(player => player.Id != owner.Id && !player.IsEliminated)
                    .OrderBy(_ => random.Next())
                    .Take(1)
                    .ToList(),
                _ => new()
            };
        }

        private void RelocatePlayerToRandomTile(Player player)
        {
            if (CurrentStage == null || CurrentStage.Map.Tiles.Count <= 1)
                return;

            List<TileData> destinations = CurrentStage.Map.Tiles
                .Where(tile => !ReferenceEquals(tile, player.CurrentTile))
                .ToList();
            if (destinations.Count == 0)
                return;

            player.CurrentTile = destinations[random.Next(destinations.Count)];
            Publish(GameMessageType.PlayerMoved, playerId: player.Id, value: GetTileIndex(player.CurrentTile));
        }

        private void SwapPlayerPositions(Player first, Player second)
        {
            (first.CurrentTile, second.CurrentTile) = (second.CurrentTile, first.CurrentTile);
            Publish(GameMessageType.PlayerMoved, playerId: first.Id, value: GetTileIndex(first.CurrentTile));
            Publish(GameMessageType.PlayerMoved, playerId: second.Id, value: GetTileIndex(second.CurrentTile));
        }

        private void ShuffleAlivePlayerPositions()
        {
            List<Player> alive = Players.Where(player => !player.IsEliminated).ToList();
            List<TileData?> tiles = alive.Select(player => player.CurrentTile).ToList();
            for (int index = tiles.Count - 1; index > 0; index--)
            {
                int swapIndex = random.Next(index + 1);
                (tiles[index], tiles[swapIndex]) = (tiles[swapIndex], tiles[index]);
            }

            for (int index = 0; index < alive.Count; index++)
            {
                alive[index].CurrentTile = tiles[index];
                Publish(GameMessageType.PlayerMoved, playerId: alive[index].Id, value: GetTileIndex(tiles[index]));
            }
        }

        private CardData? DrawRandomCard()
        {
            if (CurrentStage == null)
                return null;

            List<CardEntry> available = CurrentStage.Cards
                .Where(entry => entry?.Card != null && entry.Count > 0)
                .ToList();
            long total = available.Sum(entry => (long)entry.Count);
            if (total <= 0)
                return null;

            long ticket = random.NextInt64(total);
            foreach (CardEntry entry in available)
            {
                if (ticket < entry.Count)
                    return CloneCard(entry.Card);
                ticket -= entry.Count;
            }
            return null;
        }

        private static CardData CloneCard(CardData card)
        {
            return new CardData
            {
                Name = card.Name,
                Description = card.Description,
                Usable = card.Usable,
                Effects = (card.Effects ?? new List<EffectData>())
                    .Select(effect => new EffectData
                    {
                        Type = effect.Type,
                        Target = effect.Target,
                        Count = effect.Count,
                        Amount = effect.Amount
                    })
                    .ToList()
            };
        }

        private void ChangeHP(Player player, int delta)
        {
            if (player.IsEliminated || delta == 0)
                return;

            int oldHp = player.HP;
            if (delta < 0)
                player.TakeDamage(-delta);
            else
                player.Heal(delta);

            int change = player.HP - oldHp;
            if (change == 0)
                return;

            Log($"{player.Name}: HP {change:+0;-0}");
            if (player.IsEliminated)
                Log($"{player.Name} 탈락!");
            Publish(GameMessageType.StateChanged, playerId: player.Id, value: player.HP);
        }

        private void EndTurn()
        {
            if (!IsGameRunning || CheckGameOver())
                return;

            IsMoving = false;
            IsWaitingForPath = false;
            pendingTeleportPlayer = null;
            pendingTeleportSourceTile = -1;
            pendingCardTarget = null;
            RemainingMoves = 0;
            HasRolledThisTurn = false;

            while (IsGameRunning)
            {
                CurrentPlayerIndex++;
                if (CurrentPlayerIndex >= turnOrder.Count)
                {
                    CurrentPlayerIndex = 0;
                    CurrentRound++;
                    if (CurrentRound > MaxRound)
                    {
                        EndGameByHP();
                        return;
                    }
                }

                Player next = CurrentPlayer;
                if (next.IsEliminated)
                    continue;

                if (prisonTurns[next.Id] > 0)
                {
                    prisonTurns[next.Id]--;
                    Log($"{next.Name}: 감옥으로 턴을 건너뜁니다.");
                    continue;
                }

                Log($"{next.Name} 턴 시작");
                Publish(GameMessageType.StateChanged);
                return;
            }
        }

        private bool CheckGameOver()
        {
            if (!IsGameRunning)
                return true;

            List<Player> alive = Players.Where(player => !player.IsEliminated).ToList();
            if (alive.Count > 1)
                return false;

            EndGame(alive.SingleOrDefault());
            return true;
        }

        private void EndGameByHP()
        {
            int highestHp = Players.Max(player => player.HP);
            List<Player> leaders = Players.Where(player => player.HP == highestHp).ToList();
            EndGame(leaders.Count == 1 ? leaders[0] : null);
        }

        private void EndGame(Player? winner)
        {
            if (!IsGameRunning)
                return;

            IsGameRunning = false;
            IsMoving = false;
            IsWaitingForPath = false;
            pendingTeleportPlayer = null;
            pendingCardTarget = null;
            RemainingMoves = 0;
            HasRolledThisTurn = false;
            string message = winner == null
                ? "게임 종료: 공동 결과"
                : $"PLAYER {winner.Id} 승리!";
            Log(message);
            Publish(GameMessageType.GameEnded, winnerId: winner?.Id, text: message);
        }

        private string GetTileLabel(TileData tile)
        {
            if (ReferenceEquals(tile, CurrentStage?.Map.Start))
                return "START";

            return tile.Type switch
            {
                TileType.Damage => "DAMAGE",
                TileType.Heal => "HEAL",
                TileType.Card => "CARD",
                TileType.Prison => "PRISON",
                TileType.Teleport => "TELEPORT",
                TileType.Hole => "HOLE",
                _ => string.Empty
            };
        }

        private int GetTileIndex(TileData? tile)
        {
            if (tile == null || CurrentStage == null)
                return -1;

            int index = CurrentStage.Map.Tiles.FindIndex(candidate => ReferenceEquals(candidate, tile));
            return index >= 0
                ? index
                : CurrentStage.Map.Tiles.FindIndex(candidate => candidate.Position == tile.Position);
        }

        private GameStateSnapshot CreateSnapshot()
        {
            return new GameStateSnapshot
            {
                Players = Players.Select(player => new GamePlayerSnapshot
                {
                    Id = player.Id,
                    Name = player.Name,
                    HP = player.HP,
                    MaxHP = player.MaxHP,
                    TileIndex = GetTileIndex(player.CurrentTile),
                    PrisonTurns = prisonTurns.TryGetValue(player.Id, out int turns) ? turns : 0,
                    IsEliminated = player.IsEliminated,
                    Cards = player.Cards.Select(CloneCard).ToList()
                }).ToList(),
                TurnOrder = turnOrder.ToList(),
                CurrentPlayerId = CurrentPlayerId,
                CurrentRound = CurrentRound,
                MaximumRound = MaxRound,
                PreferredDiceChance = Players.Count == 0
                    ? 0
                    : DiceProbability.PreferredChance(CurrentPlayer.HP, CurrentPlayer.MaxHP),
                IsSuddenDeath = CurrentRound >= SuddenDeathRound,
                RemainingMoves = RemainingMoves,
                IsGameRunning = IsGameRunning,
                IsMoving = IsMoving,
                HasRolledThisTurn = HasRolledThisTurn,
                IsWaitingForPath = IsWaitingForPath,
                IsWaitingForTeleport = IsWaitingForTeleport,
                IsWaitingForCardTarget = IsWaitingForCardTarget
            };
        }

        private void Publish(
            GameMessageType type,
            string text = "",
            int playerId = -1,
            int value = 0,
            int? winnerId = null,
            int cardIndex = -1,
            bool isImmediateCard = false,
            List<int>? optionIndices = null)
        {
            if (disposed)
                return;

            connector.Fire(GameConnectorNames.MainGame, new GameManagerMessage
            {
                Type = type,
                State = CreateSnapshot(),
                Text = text,
                PlayerId = playerId,
                Value = value,
                WinnerId = winnerId,
                CardIndex = cardIndex,
                IsImmediateCard = isImmediateCard,
                OptionIndices = optionIndices ?? new List<int>()
            });
        }

        private void Log(string message)
        {
            Publish(GameMessageType.Log, text: message);
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            connector.Destroy();
        }
    }
}
