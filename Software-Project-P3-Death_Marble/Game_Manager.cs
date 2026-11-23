using System;
using System.Collections.Generic;
using System.Linq;

namespace Software_Project_P3_Death_Marble
{
    internal class Game_Manager
    {
        // ========================================
        // 게임 기본 설정
        // ========================================

        private const int MAX_ROUND = 40;
        private const int SUDDEN_DEATH_ROUND = 21;

        private readonly Random random = new Random();

        // 플레이어 목록
        public List<Player> Players { get; private set; }
            = new List<Player>();

        // 현재 스테이지
        public StageData? CurrentStage { get; private set; }

        // 현재 플레이어 인덱스
        public int CurrentPlayerIndex { get; private set; } = 0;

        // 현재 라운드
        public int CurrentRound { get; private set; } = 1;

        // 남은 이동 횟수
        public int RemainingMoves { get; private set; } = 0;

        // 게임 진행 여부
        public bool IsGameRunning { get; private set; } = false;

        // 갈림길 선택 대기 여부
        public bool IsWaitingForPath { get; private set; } = false;

        // 현재 플레이어
        public Player CurrentPlayer => Players[CurrentPlayerIndex];

        // ========================================
        // Connector 이벤트
        // ========================================

        private readonly Connector.BindableEvent loadStageEv;
        private readonly Connector.BindableEvent loadGameEv;

        // ========================================
        // GUI 이벤트
        // ========================================

        public event Action? GameStateChanged;

        public event Action<Player>? TurnStarted;

        public event Action<Player, int>? DiceRolled;

        public event Action<Player, TileData>? PlayerMoved;

        public event Action<Player, List<TileData>>? PathSelectionRequired;

        public event Action<Player, TileData>? TileEffectApplied;

        public event Action<Player>? PlayerEliminated;

        public event Action<Player?>? GameEnded;

        public event Action<string>? GameLog;

        // ========================================
        // 생성자
        // ========================================

        public Game_Manager()
        {
            loadStageEv =
                Connector.GetBindableEvent("loadStageEv");

            loadStageEv.Name = "GameManagerrEv";

            loadGameEv =
                Connector.GetBindableEvent("loadGame");
        }

        // ========================================
        // 1. 게임 초기화
        // ========================================

        public void InitializeGame(
            StageData stage,
            int playerCount,
            int botCount = 0)
        {
            if (stage == null)
                throw new ArgumentNullException(nameof(stage));

            if (playerCount < 2 || playerCount > 4)
                throw new ArgumentException(
                    "플레이어 수는 2~4명이어야 합니다.");

            if (botCount < 0 || botCount >= playerCount)
                throw new ArgumentException(
                    "봇 수가 올바르지 않습니다.");

            if (stage.Map.Start == null)
                throw new InvalidOperationException(
                    "스테이지에 시작 타일이 없습니다.");

            Players.Clear();

            CurrentStage = stage;

            for (int i = 0; i < playerCount; i++)
            {
                bool isBot = i >= playerCount - botCount;

                Player player = new Player(
                    i + 1,
                    $"PLAYER {i + 1}",
                    stage.Map.Start,
                    isBot);

                Players.Add(player);
            }

            CurrentPlayerIndex = 0;
            CurrentRound = 1;
            RemainingMoves = 0;

            IsGameRunning = true;
            IsWaitingForPath = false;

            Log("게임이 초기화되었습니다.");

            StartTurn();

            NotifyStateChanged();
        }

        // ========================================
        // 2. 턴 시작
        // ========================================

        private void StartTurn()
        {
            if (!IsGameRunning)
                return;

            Player player = CurrentPlayer;

            RemainingMoves = 0;
            IsWaitingForPath = false;

            Log($"{player.Name}의 턴입니다.");

            TurnStarted?.Invoke(player);

            NotifyStateChanged();
        }

        // ========================================
        // 3. 주사위 굴리기
        // ========================================

        public int RollDice()
        {
            if (!IsGameRunning)
                throw new InvalidOperationException(
                    "게임이 진행 중이지 않습니다.");

            if (RemainingMoves > 0 || IsWaitingForPath)
                throw new InvalidOperationException(
                    "이전 이동이 끝나지 않았습니다.");

            int dice = random.Next(1, 7);

            RemainingMoves = dice;

            Log($"{CurrentPlayer.Name} 주사위 결과: {dice}");

            DiceRolled?.Invoke(CurrentPlayer, dice);

            NotifyStateChanged();

            return dice;
        }

        // ========================================
        // 4. 플레이어 이동
        // ========================================

        public void MovePlayer()
        {
            if (!IsGameRunning)
                return;

            if (RemainingMoves <= 0)
                return;

            if (IsWaitingForPath)
                return;

            while (RemainingMoves > 0)
            {
                TileData? currentTile =
                    CurrentPlayer.CurrentTile;

                if (currentTile == null)
                    return;

                List<TileData> nextTiles =
                    currentTile.Next;

                // 이동 가능한 타일이 없는 경우
                if (nextTiles.Count == 0)
                {
                    Log("더 이상 이동할 수 없습니다.");

                    RemainingMoves = 0;
                    break;
                }

                // 갈림길
                if (nextTiles.Count > 1)
                {
                    IsWaitingForPath = true;

                    Log("갈림길에 도착했습니다.");

                    PathSelectionRequired?.Invoke(
                        CurrentPlayer,
                        nextTiles.ToList());

                    NotifyStateChanged();

                    return;
                }

                // 일반 이동
                MoveToTile(nextTiles[0]);
            }

            FinishMovement();
        }

        // ========================================
        // 5. 갈림길 선택
        // ========================================

        public void SelectPath(int pathIndex)
        {
            if (!IsWaitingForPath)
                return;

            TileData? currentTile =
                CurrentPlayer.CurrentTile;

            if (currentTile == null)
                return;

            List<TileData> nextTiles =
                currentTile.Next;

            if (pathIndex < 0 ||
                pathIndex >= nextTiles.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pathIndex));
            }

            IsWaitingForPath = false;

            TileData selectedTile =
                nextTiles[pathIndex];

            Log(
                $"{CurrentPlayer.Name}이(가) " +
                $"{pathIndex + 1}번 경로를 선택했습니다.");

            MoveToTile(selectedTile);

            MovePlayer();
        }

        // ========================================
        // 6. 실제 한 칸 이동
        // ========================================

        private void MoveToTile(TileData nextTile)
        {
            CurrentPlayer.CurrentTile = nextTile;

            RemainingMoves--;

            PlayerMoved?.Invoke(
                CurrentPlayer,
                nextTile);

            NotifyStateChanged();
        }

        // ========================================
        // 7. 이동 종료
        // ========================================

        private void FinishMovement()
        {
            if (RemainingMoves > 0)
                return;

            ApplyTileEffect();

            if (!IsGameRunning)
                return;

            EndTurn();
        }

        // ========================================
        // 8. 타일 이벤트
        // ========================================

        private void ApplyTileEffect()
        {
            Player player = CurrentPlayer;

            TileData? tile = player.CurrentTile;

            if (tile == null)
                return;

            switch (tile.Type)
            {
                case TileType.Zone:

                    Log("일반 타일에 도착했습니다.");
                    break;

                case TileType.Damage:

                    player.TakeDamage(tile.Amount);

                    Log(
                        $"{player.Name} HP -{tile.Amount}");
                    break;

                case TileType.Heal:

                    player.Heal(tile.Amount);

                    Log(
                        $"{player.Name} HP +{tile.Amount}");
                    break;

                case TileType.Card:

                    GiveRandomCard(player);
                    break;

                case TileType.Prison:

                    Log(
                        $"{player.Name}이(가) 감옥에 도착했습니다.");
                    break;

                case TileType.Teleport:

                    Log(
                        $"{player.Name}이(가) 텔레포트 타일에 도착했습니다.");
                    break;

                case TileType.Hole:

                    player.TakeDamage(tile.Amount);

                    Log(
                        $"{player.Name}이(가) 함정에 빠졌습니다.");
                    break;
            }

            TileEffectApplied?.Invoke(player, tile);

            if (player.IsEliminated)
            {
                Log($"{player.Name} 탈락!");

                PlayerEliminated?.Invoke(player);
            }

            NotifyStateChanged();

            CheckGameOver();
        }

        // ========================================
        // 9. 카드 획득
        // ========================================

        private void GiveRandomCard(Player player)
        {
            if (CurrentStage == null)
                return;

            List<CardEntry> cards =
                CurrentStage.Cards;

            if (cards.Count == 0)
            {
                Log("획득할 카드가 없습니다.");
                return;
            }

            List<CardData> availableCards =
                new List<CardData>();

            foreach (CardEntry entry in cards)
            {
                for (int i = 0; i < entry.Count; i++)
                {
                    availableCards.Add(entry.Card);
                }
            }

            if (availableCards.Count == 0)
                return;

            CardData card =
                availableCards[
                    random.Next(availableCards.Count)];

            if (player.AddCard(card))
            {
                Log(
                    $"{player.Name} 카드 획득: {card.Name}");
            }
            else
            {
                Log("카드는 최대 3장까지 보유할 수 있습니다.");
            }
        }

        // ========================================
        // 10. 카드 사용
        // ========================================

        public void UseCard(int cardIndex)
        {
            if (!IsGameRunning)
                return;

            Player player = CurrentPlayer;

            if (cardIndex < 0 ||
                cardIndex >= player.Cards.Count)
            {
                return;
            }

            CardData card = player.Cards[cardIndex];

            foreach (EffectData effect in card.Effects)
            {
                switch (effect.Type)
                {
                    case EffectType.Heal:

                        player.Heal(effect.Amount);
                        break;

                    case EffectType.Damage:

                        player.TakeDamage(effect.Amount);
                        break;
                }
            }

            player.RemoveCard(card);

            Log($"{player.Name} 카드 사용: {card.Name}");

            if (player.IsEliminated)
            {
                PlayerEliminated?.Invoke(player);
            }

            NotifyStateChanged();

            CheckGameOver();
        }

        // ========================================
        // 11. 턴 종료
        // ========================================

        private void EndTurn()
        {
            if (!IsGameRunning)
                return;

            int checkedCount = 0;

            do
            {
                CurrentPlayerIndex++;

                if (CurrentPlayerIndex >= Players.Count)
                {
                    CurrentPlayerIndex = 0;

                    CurrentRound++;

                    ApplySuddenDeath();
                }

                checkedCount++;

            } while (
                CurrentPlayer.IsEliminated &&
                checkedCount < Players.Count);

            if (CheckGameOver())
                return;

            StartTurn();
        }

        // ========================================
        // 12. 서든 데스
        // ========================================

        private void ApplySuddenDeath()
        {
            if (CurrentRound < SUDDEN_DEATH_ROUND)
                return;

            int damage = 5 +
                ((CurrentRound - SUDDEN_DEATH_ROUND) / 5) * 5;

            foreach (Player player in Players)
            {
                if (!player.IsEliminated)
                {
                    player.TakeDamage(damage);
                }
            }

            Log(
                $"서든 데스! 모든 플레이어 HP -{damage}");

            NotifyStateChanged();
        }

        // ========================================
        // 13. 게임 종료 판정
        // ========================================

        private bool CheckGameOver()
        {
            if (!IsGameRunning)
                return true;

            List<Player> alivePlayers =
                Players.Where(
                    p => !p.IsEliminated).ToList();

            if (alivePlayers.Count <= 1)
            {
                Player? winner =
                    alivePlayers.FirstOrDefault();

                EndGame(winner);

                return true;
            }

            if (CurrentRound > MAX_ROUND)
            {
                Player winner =
                    Players.OrderByDescending(
                        p => p.HP).First();

                EndGame(winner);

                return true;
            }

            return false;
        }

        // ========================================
        // 14. 게임 종료
        // ========================================

        private void EndGame(Player? winner)
        {
            IsGameRunning = false;

            RemainingMoves = 0;
            IsWaitingForPath = false;

            if (winner != null)
            {
                Log($"게임 종료! 승리자: {winner.Name}");
            }
            else
            {
                Log("게임 종료! 생존자가 없습니다.");
            }

            GameEnded?.Invoke(winner);

            NotifyStateChanged();
        }

        // ========================================
        // 15. 공통 이벤트
        // ========================================

        private void NotifyStateChanged()
        {
            GameStateChanged?.Invoke();
        }

        private void Log(string message)
        {
            GameLog?.Invoke(message);
        }
    }
}