using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Software_Project_P3_Death_Marble.Views
{
    public partial class MainGame : UserControl
    {
        private readonly int playerCount;
        private readonly List<int> turnOrder;
        private readonly StageData stage;
        private readonly Game_Manager gameManager;
        private readonly Connector.BindableEvent gameConnector;

        private readonly Dictionary<int, PixelCharacter> characters = new();
        private readonly Dictionary<int, ProgressBar> characterHealthBars = new();
        private readonly Dictionary<int, int> playerPositions = new();
        private readonly Dictionary<int, int> playerHP = new();
        private readonly Dictionary<int, List<CardData>> playerCards = new();
        private readonly Dictionary<int, int> prisonTurns = new();
        private readonly List<Point> tilePositions = new();
        private GameStateSnapshot? gameState;

        private readonly ScaleTransform boardScale = new(1, 1);
        private readonly TranslateTransform boardTranslation = new();
        private readonly TransformGroup boardTransform = new();

        private DispatcherTimer? moveTimer;
        private readonly DispatcherTimer rollHoldTimer;
        private int pendingTargetCardIndex = -1;
        private int pendingTargetOwner = -1;
        private bool pendingTargetIsImmediate;

        private int turnIndex;
        private int currentRound = 1;
        private int movingPlayer;
        private int? pendingTeleportPlayer;
        private int pendingTeleportSourceTile = -1;

        private bool isMoving;
        private bool isRolling;
        private bool isClosing;
        private bool connectionsDisposed;
        private bool gameEnded;
        private bool hasInitialBoardView;
        private bool isDraggingBoard;
        private bool hasRolledThisTurn;
        private bool isRollPressActive;
        private bool isChoosingDice;
        private bool isTeleportClickCandidate;

        private int? hoveredDiceChoice;

        private Point dragStart;
        private double dragTranslationX;
        private double dragTranslationY;

        private const int MaxHP = 100;
        private const int TileSize = 88;
        private const double TileSpacing = 112;
        private const double BoardMargin = 56;
        private const double MinimumVisibleTileSize = 28;
        private const double MinimumZoom = MinimumVisibleTileSize / TileSize;
        private const double MaximumZoom = 3.0;

        public event EventHandler? ExitRequested;
        public event EventHandler<int?>? GameFinished;

        public MainGame()
            : this(
                2,
                new List<int> { 1, 2 },
                new StageData
                {
                    Name = "기본 스테이지",
                    Map = MapData.CreateDefault()
                })
        {
        }

        public MainGame(int playerCount, List<int> turnOrder)
            : this(
                playerCount,
                turnOrder,
                new StageData
                {
                    Name = "기본 스테이지",
                    Map = MapData.CreateDefault()
                })
        {
        }

        public MainGame(int playerCount, List<int> turnOrder, StageData stage)
        {
            InitializeComponent();

            if (playerCount < 2 || playerCount > 4)
                throw new ArgumentOutOfRangeException(nameof(playerCount));

            if (turnOrder == null ||
                turnOrder.Count != playerCount ||
                turnOrder.Distinct().Count() != playerCount ||
                turnOrder.Any(player => player < 1 || player > playerCount))
            {
                throw new ArgumentException(
                    "올바른 턴 순서가 아닙니다.",
                    nameof(turnOrder));
            }

            this.playerCount = playerCount;
            this.turnOrder = new List<int>(turnOrder);
            this.stage = stage ?? throw new ArgumentNullException(nameof(stage));
            BoardNameText.Text = string.IsNullOrWhiteSpace(stage.Name)
                ? "이름 없는 스테이지"
                : stage.Name;

            if (stage.Map.Start == null)
                throw new ArgumentException("스테이지에 시작 타일이 없습니다.", nameof(stage));

            if (stage.Map.Tiles.Count == 0)
                stage.Map.Tiles.Add(stage.Map.Start);

            if (!stage.Map.Tiles.Any(tile => ReferenceEquals(tile, stage.Map.Start)))
                stage.Map.Tiles.Insert(0, stage.Map.Start);

            gameManager = new Game_Manager();
            gameConnector = Connector.GetBindableEvent(GameConnectorNames.Bus);
            gameConnector.Name = GameConnectorNames.MainGame;
            gameConnector.OnInvoke = GameConnector_OnInvoke;

            GameDice.RollCompleted += GameDice_RollCompleted;

            rollHoldTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            rollHoldTimer.Tick += RollHoldTimer_Tick;

            boardTransform.Children.Add(boardScale);
            boardTransform.Children.Add(boardTranslation);
            BoardCanvas.RenderTransform = boardTransform;

            Loaded += (_, _) =>
            {
                FitBoardToViewport();
                RefreshHand();
            };

            CreateBoard();
            CreateCharacters();
            CreateDiceChoiceButtons();
            SendGameCommand(new GameCommand
            {
                Type = GameCommandType.Initialize,
                Stage = this.stage,
                TurnOrder = this.turnOrder.ToList()
            });
        }

        private void SendGameCommand(GameCommand command)
        {
            if (!isClosing)
                gameConnector.Fire(GameConnectorNames.GameManager, command);
        }

        private void GameConnector_OnInvoke(string sender, dynamic? payload)
        {
            if (payload is not GameManagerMessage message || isClosing)
                return;

            bool isFirstState = gameState == null;
            ApplyGameState(message.State);

            switch (message.Type)
            {
                case GameMessageType.Log:
                    AddGameLog(message.Text);
                    break;
                case GameMessageType.DiceRolled:
                    DiceText.Text = "ROLLING...";
                    GameDice.Roll(message.Value);
                    break;
                case GameMessageType.PlayerMoved:
                    PathChoiceOverlay.Visibility = Visibility.Collapsed;
                    if (characters.TryGetValue(message.PlayerId, out PixelCharacter? movingCharacter))
                    {
                        if (gameState?.IsMoving == true)
                            movingCharacter.StartWalking();
                        else
                            movingCharacter.StopWalking();
                    }
                    AnimateCharacterToTile(message.PlayerId, message.Value);
                    if (gameState?.IsMoving == true)
                        moveTimer?.Start();
                    break;
                case GameMessageType.MovementFinished:
                    moveTimer?.Stop();
                    if (characters.TryGetValue(message.PlayerId, out PixelCharacter? arrivedCharacter))
                        arrivedCharacter.StopWalking();
                    break;
                case GameMessageType.PathSelectionRequired:
                    ShowPathChoices(message.OptionIndices);
                    break;
                case GameMessageType.TeleportSelectionRequired:
                    pendingTeleportPlayer = message.PlayerId;
                    pendingTeleportSourceTile = message.Value;
                    TeleportTargetText.Text = $"PLAYER {message.PlayerId}: 맵을 드래그해 이동한 뒤 목적지 타일을 클릭하세요.";
                    TeleportTargetOverlay.Visibility = Visibility.Visible;
                    BoardViewport.Cursor = Cursors.Cross;
                    break;
                case GameMessageType.CardTargetSelectionRequired:
                    ShowCardTargetSelection(message);
                    break;
                case GameMessageType.GameEnded:
                    StopAnimations();
                    DisposeGameConnections();
                    GameFinished?.Invoke(this, message.WinnerId);
                    break;
                case GameMessageType.Error:
                    AddGameLog($"게임 처리 오류: {message.Text}");
                    break;
            }

            if (isFirstState)
                UpdateAllCharacterPositions();

            UpdatePlayerStatus();
            UpdateTurnDisplay();
            RefreshHand();
            UpdateRollButtonState();
        }

        private void ApplyGameState(GameStateSnapshot? snapshot)
        {
            if (snapshot == null)
                return;

            Dictionary<int, int> previousHp = playerHP.ToDictionary(entry => entry.Key, entry => entry.Value);

            gameState = snapshot;
            currentRound = snapshot.CurrentRound;
            turnIndex = Math.Max(0, snapshot.TurnOrder.IndexOf(snapshot.CurrentPlayerId));
            movingPlayer = snapshot.CurrentPlayerId;
            isMoving = snapshot.IsMoving;
            hasRolledThisTurn = snapshot.HasRolledThisTurn;
            gameEnded = !snapshot.IsGameRunning;

            playerPositions.Clear();
            playerHP.Clear();
            playerCards.Clear();
            prisonTurns.Clear();
            foreach (GamePlayerSnapshot player in snapshot.Players)
            {
                playerPositions[player.Id] = player.TileIndex;
                playerHP[player.Id] = player.HP;
                playerCards[player.Id] = player.Cards;
                prisonTurns[player.Id] = player.PrisonTurns;

                if (previousHp.TryGetValue(player.Id, out int oldHp))
                {
                    int hpChange = player.HP - oldHp;
                    if (hpChange != 0)
                    {
                        if (characters.TryGetValue(player.Id, out PixelCharacter? character))
                        {
                            if (hpChange < 0)
                                character.PlayDamageEffect();
                            else
                                character.PlayHealEffect();
                        }
                        ShowHPChangeText(player.Id, hpChange);
                    }

                }

                if (characterHealthBars.TryGetValue(player.Id, out ProgressBar? healthBar))
                {
                    healthBar.Value = player.HP;
                    healthBar.ToolTip = $"PLAYER {player.Id} · HP {player.HP} / {player.MaxHP}";
                    if (player.IsEliminated)
                        healthBar.Visibility = Visibility.Collapsed;
                }
                if (player.IsEliminated && characters.TryGetValue(player.Id, out PixelCharacter? eliminated))
                    eliminated.Visibility = Visibility.Collapsed;
            }

            if (!snapshot.IsWaitingForTeleport)
            {
                pendingTeleportPlayer = null;
                pendingTeleportSourceTile = -1;
                TeleportTargetOverlay.Visibility = Visibility.Collapsed;
                BoardViewport.Cursor = Cursors.Hand;
            }

            if (!snapshot.IsWaitingForCardTarget && pendingTargetOwner >= 0)
            {
                pendingTargetCardIndex = -1;
                pendingTargetOwner = -1;
                pendingTargetIsImmediate = false;
                CardTargetOverlay.Visibility = Visibility.Collapsed;
                CardTargetButtons.Children.Clear();
            }
        }

        private Brush GetPlayerColor(int player)
        {
            return player switch
            {
                1 => Brushes.IndianRed,
                2 => Brushes.DodgerBlue,
                3 => Brushes.LimeGreen,
                4 => Brushes.Gold,
                _ => Brushes.White
            };
        }

        private void CreateBoard()
        {
            BoardCanvas.Children.Clear();
            tilePositions.Clear();

            List<TileData> mapTiles = stage.Map.Tiles;
            TileData startTile = stage.Map.Start!;
            double startX = startTile.Position.X;
            double startY = startTile.Position.Y;

            List<(double X, double Y)> normalized = mapTiles
                .Select(tile => (
                    (double)tile.Position.X - startX,
                    (double)tile.Position.Y - startY))
                .ToList();

            double minX = normalized.Min(position => position.Item1);
            double maxX = normalized.Max(position => position.Item1);
            double minY = normalized.Min(position => position.Item2);
            double maxY = normalized.Max(position => position.Item2);

            for (int i = 0; i < mapTiles.Count; i++)
            {
                Point position = new(
                    BoardMargin + (normalized[i].X - minX) * TileSpacing,
                    BoardMargin + (normalized[i].Y - minY) * TileSpacing);
                tilePositions.Add(position);
            }

            BoardCanvas.Width = Math.Max(
                TileSize + BoardMargin * 2,
                (maxX - minX) * TileSpacing + TileSize + BoardMargin * 2);

            BoardCanvas.Height = Math.Max(
                TileSize + BoardMargin * 2,
                (maxY - minY) * TileSpacing + TileSize + BoardMargin * 2);

            Dictionary<TileData, int> tileIndexes = mapTiles
                .Select((tile, index) => (tile, index))
                .ToDictionary(item => item.tile, item => item.index);

            HashSet<(int From, int To)> drawnConnections = new();

            for (int i = 0; i < mapTiles.Count; i++)
            {
                foreach (TileData next in mapTiles[i].Next)
                {
                    if (!tileIndexes.TryGetValue(next, out int nextIndex))
                    {
                        nextIndex = mapTiles.FindIndex(
                            tile => tile.Position == next.Position);
                    }

                    if (nextIndex < 0 || nextIndex == i)
                        continue;

                    if (!drawnConnections.Add((i, nextIndex)))
                        continue;

                    Point fromCenter = new(
                        tilePositions[i].X + TileSize / 2.0,
                        tilePositions[i].Y + TileSize / 2.0);

                    Point toCenter = new(
                        tilePositions[nextIndex].X + TileSize / 2.0,
                        tilePositions[nextIndex].Y + TileSize / 2.0);

                    bool hasReverseConnection = mapTiles[nextIndex].Next.Any(reverse =>
                        ReferenceEquals(reverse, mapTiles[i]) ||
                        reverse.Position == mapTiles[i].Position);

                    DrawConnectionArrow(
                        BoardCanvas,
                        fromCenter,
                        toCenter,
                        TileSize,
                        Brushes.DarkGoldenrod,
                        7,
                        hasReverseConnection ? 5 : 0);
                }
            }

            for (int i = 0; i < mapTiles.Count; i++)
            {
                Border tile = CreateTile(mapTiles[i]);
                Point point = tilePositions[i];

                Canvas.SetLeft(tile, point.X);
                Canvas.SetTop(tile, point.Y);
                BoardCanvas.Children.Add(tile);
            }
        }

        private static void DrawConnectionArrow(
            Canvas canvas,
            Point fromCenter,
            Point toCenter,
            double tileSize,
            Brush brush,
            double strokeThickness,
            double laneOffset)
        {
            double deltaX = toCenter.X - fromCenter.X;
            double deltaY = toCenter.Y - fromCenter.Y;
            double distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);

            if (distance <= 0)
                return;

            double directionX = deltaX / distance;
            double directionY = deltaY / distance;
            double normalX = -directionY;
            double normalY = directionX;
            double edgeDistance = tileSize / 2 + 1;

            Point lineStart = new(
                fromCenter.X + directionX * edgeDistance + normalX * laneOffset,
                fromCenter.Y + directionY * edgeDistance + normalY * laneOffset);

            Point arrowTip = new(
                toCenter.X - directionX * edgeDistance + normalX * laneOffset,
                toCenter.Y - directionY * edgeDistance + normalY * laneOffset);

            const double arrowLength = 13;
            const double arrowHalfWidth = 6;

            Point arrowBase = new(
                arrowTip.X - directionX * arrowLength,
                arrowTip.Y - directionY * arrowLength);

            Point arrowSide1 = new(
                arrowBase.X + normalX * arrowHalfWidth,
                arrowBase.Y + normalY * arrowHalfWidth);

            Point arrowSide2 = new(
                arrowBase.X - normalX * arrowHalfWidth,
                arrowBase.Y - normalY * arrowHalfWidth);

            canvas.Children.Add(new Line
            {
                X1 = lineStart.X,
                Y1 = lineStart.Y,
                X2 = arrowBase.X,
                Y2 = arrowBase.Y,
                Stroke = brush,
                StrokeThickness = strokeThickness,
                IsHitTestVisible = false
            });

            canvas.Children.Add(new Polygon
            {
                Points = new PointCollection { arrowTip, arrowSide1, arrowSide2 },
                Fill = brush,
                IsHitTestVisible = false
            });
        }

        private Border CreateTile(TileData tile)
        {
            bool isStart = ReferenceEquals(tile, stage.Map.Start);

            (Brush background, string label) = tile.Type switch
            {
                TileType.Damage => (Brushes.Maroon, $"-{tile.Amount} HP"),
                TileType.Heal => (Brushes.DarkGreen, $"+{tile.Amount} HP"),
                TileType.Card => (Brushes.DarkSlateBlue, "CARD"),
                TileType.Prison => (Brushes.SaddleBrown, "PRISON"),
                TileType.Teleport => (Brushes.DarkCyan, "PORTAL"),
                TileType.Hole => (Brushes.Black, "HOLE"),
                _ => (new SolidColorBrush(Color.FromRgb(53, 35, 41)), string.Empty)
            };

            if (isStart)
                label = "START";

            Border border = new Border
            {
                Width = TileSize,
                Height = TileSize,
                Background = background,
                BorderBrush = isStart ? Brushes.Gold : Brushes.Goldenrod,
                BorderThickness = new Thickness(isStart ? 4 : 3)
            };

            Grid grid = new Grid();
            if (!string.IsNullOrWhiteSpace(label))
            {
                grid.Children.Add(new TextBlock
                {
                    Text = label,
                    Foreground = Brushes.White,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 15,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4),
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center
                });
            }

            border.Child = grid;
            border.ToolTip = string.IsNullOrWhiteSpace(label)
                ? $"({tile.Position.X}, {tile.Position.Y})"
                : $"{label} · ({tile.Position.X}, {tile.Position.Y})";

            return border;
        }

        private void CreateCharacters()
        {
            characters.Clear();
            characterHealthBars.Clear();

            for (int player = 1; player <= playerCount; player++)
            {
                PixelCharacter character = new(player);
                characters[player] = character;
                Panel.SetZIndex(character, 100);
                BoardCanvas.Children.Add(character);

                ProgressBar healthBar = new()
                {
                    Width = 56,
                    Height = 8,
                    Minimum = 0,
                    Maximum = MaxHP,
                    Value = MaxHP,
                    Foreground = GetPlayerColor(player),
                    Background = Brushes.DarkSlateGray,
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(1),
                    ToolTip = $"PLAYER {player} · HP {MaxHP} / {MaxHP}",
                    IsHitTestVisible = false
                };
                characterHealthBars[player] = healthBar;
                Panel.SetZIndex(healthBar, 110);
                BoardCanvas.Children.Add(healthBar);
            }

            UpdateAllCharacterPositions();
        }

        private void UpdateAllCharacterPositions()
        {
            foreach (int player in characters.Keys)
                UpdateCharacterPosition(player);
        }

        private void UpdateCharacterPosition(int player)
        {
            if (!characters.TryGetValue(player, out PixelCharacter? character) ||
                !playerPositions.TryGetValue(player, out int position) ||
                position < 0 ||
                position >= tilePositions.Count)
            {
                return;
            }

            Point point = GetCharacterPosition(player, position);
            Canvas.SetLeft(character, point.X);
            Canvas.SetTop(character, point.Y);

            if (characterHealthBars.TryGetValue(player, out ProgressBar? healthBar))
            {
                Canvas.SetLeft(healthBar, point.X - 4);
                Canvas.SetTop(healthBar, point.Y - 10);
            }
        }

        private void QueueCardTargetSelection(int targetPlayer, int cardIndex, int ownerPlayer, bool isImmediate)
        {
            foreach (Button button in CardTargetButtons.Children.OfType<Button>())
                button.IsEnabled = false;

            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (isClosing || gameEnded ||
                    pendingTargetCardIndex != cardIndex ||
                    pendingTargetOwner != ownerPlayer ||
                    pendingTargetIsImmediate != isImmediate)
                {
                    return;
                }

                SendGameCommand(new GameCommand
                {
                    Type = GameCommandType.SelectCardTarget,
                    PlayerId = targetPlayer
                });
            }));
        }
        private Point GetCharacterPosition(int player, int tileIndex)
        {
            Point tile = tilePositions[tileIndex];
            int offsetIndex = player - 1;
            double offsetX = offsetIndex % 2 == 0 ? 4 : TileSize - 48;
            double offsetY = offsetIndex < 2 ? TileSize / 2.0 - 27 : TileSize - 54;

            return new Point(tile.X + offsetX, tile.Y + offsetY);
        }

        private int GetTileIndex(TileData tile)
        {
            int index = stage.Map.Tiles.FindIndex(
                candidate => ReferenceEquals(candidate, tile));

            if (index >= 0)
                return index;

            return stage.Map.Tiles.FindIndex(
                candidate => candidate.Position == tile.Position);
        }

        private void AnimateCharacterToTile(int player, int tileIndex)
        {
            if (!characters.TryGetValue(player, out PixelCharacter? character))
                return;

            Point from = new(Canvas.GetLeft(character), Canvas.GetTop(character));
            Point to = GetCharacterPosition(player, tileIndex);

            Duration duration = TimeSpan.FromMilliseconds(380);
            CubicEase easing = new() { EasingMode = EasingMode.EaseOut };

            AnimateCanvasElement(character, from, to, duration, easing);
            if (characterHealthBars.TryGetValue(player, out ProgressBar? healthBar))
                AnimateCanvasElement(healthBar, new Point(Canvas.GetLeft(healthBar), Canvas.GetTop(healthBar)), new Point(to.X - 4, to.Y - 10), duration, easing);

            FocusCameraOnTile(tileIndex, duration);
        }

        private static void AnimateCanvasElement(
            UIElement element,
            Point from,
            Point to,
            Duration duration,
            IEasingFunction easing)
        {
            if (double.IsNaN(from.X) || double.IsNaN(from.Y))
                from = to;

            Canvas.SetLeft(element, to.X);
            Canvas.SetTop(element, to.Y);
            element.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation
            {
                From = from.X,
                To = to.X,
                Duration = duration,
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop
            });
            element.BeginAnimation(Canvas.TopProperty, new DoubleAnimation
            {
                From = from.Y,
                To = to.Y,
                Duration = duration,
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop
            });
        }

        private void FitBoardToViewport()
        {
            if (hasInitialBoardView ||
                BoardViewport.ActualWidth <= 1 ||
                BoardViewport.ActualHeight <= 1 ||
                BoardCanvas.Width <= 1 ||
                BoardCanvas.Height <= 1)
            {
                return;
            }

            double fitScale = Math.Min(
                1,
                Math.Min(
                    BoardViewport.ActualWidth / BoardCanvas.Width,
                    BoardViewport.ActualHeight / BoardCanvas.Height));

            double minimumScale = MinimumVisibleTileSize / TileSize;
            double scale = Math.Max(fitScale, minimumScale);

            boardScale.ScaleX = scale;
            boardScale.ScaleY = scale;

            bool boardFits = fitScale >= minimumScale;

            Point focus = boardFits
                ? new Point(BoardCanvas.Width / 2, BoardCanvas.Height / 2)
                : GetTileCenter(GetTileIndex(stage.Map.Start!));

            boardTranslation.X = BoardViewport.ActualWidth / 2 - focus.X * scale;
            boardTranslation.Y = BoardViewport.ActualHeight / 2 - focus.Y * scale;
            hasInitialBoardView = true;
        }

        private Point GetTileCenter(int tileIndex)
        {
            Point tile = tilePositions[tileIndex];
            return new Point(tile.X + TileSize / 2.0, tile.Y + TileSize / 2.0);
        }

        private void FocusCameraOnTile(int tileIndex, Duration duration)
        {
            if (BoardViewport.ActualWidth <= 1 || BoardViewport.ActualHeight <= 1)
                return;

            Point center = GetTileCenter(tileIndex);
            double targetX = BoardViewport.ActualWidth / 2 -
                             center.X * boardScale.ScaleX;
            double targetY = BoardViewport.ActualHeight / 2 -
                             center.Y * boardScale.ScaleY;

            AnimateCameraTo(targetX, targetY, duration);
        }

        private void AnimateCameraTo(double targetX, double targetY, Duration duration)
        {
            double fromX = boardTranslation.X;
            double fromY = boardTranslation.Y;

            boardTranslation.X = targetX;
            boardTranslation.Y = targetY;

            CubicEase easing = new() { EasingMode = EasingMode.EaseOut };

            boardTranslation.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation
            {
                From = fromX,
                To = targetX,
                Duration = duration,
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop
            });

            boardTranslation.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation
            {
                From = fromY,
                To = targetY,
                Duration = duration,
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop
            });
        }

        private void BoardViewport_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            FitBoardToViewport();
        }

        private void BoardViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (pendingTeleportPlayer.HasValue)
            {
                e.Handled = true;
                StopCameraAnimation();
                isDraggingBoard = true;
                isTeleportClickCandidate = true;
                dragStart = e.GetPosition(BoardViewport);
                dragTranslationX = boardTranslation.X;
                dragTranslationY = boardTranslation.Y;
                BoardViewport.CaptureMouse();
                BoardViewport.Cursor = Cursors.SizeAll;
                return;
            }

            StopCameraAnimation();
            isDraggingBoard = true;
            dragStart = e.GetPosition(BoardViewport);
            dragTranslationX = boardTranslation.X;
            dragTranslationY = boardTranslation.Y;
            BoardViewport.CaptureMouse();
            BoardViewport.Cursor = Cursors.SizeAll;
            e.Handled = true;
        }

        private int GetTileIndexAtViewportPoint(Point viewportPoint)
        {
            double scale = boardScale.ScaleX;
            if (scale <= 0)
                return -1;

            Point boardPoint = new(
                (viewportPoint.X - boardTranslation.X) / scale,
                (viewportPoint.Y - boardTranslation.Y) / scale);

            for (int index = 0; index < tilePositions.Count; index++)
            {
                Point tile = tilePositions[index];
                if (boardPoint.X >= tile.X && boardPoint.X <= tile.X + TileSize &&
                    boardPoint.Y >= tile.Y && boardPoint.Y <= tile.Y + TileSize)
                {
                    return index;
                }
            }

            return -1;
        }

        private void QueueTeleportDestination(int destination)
        {
            if (pendingTeleportPlayer == null ||
                destination < 0 ||
                destination >= stage.Map.Tiles.Count ||
                destination == pendingTeleportSourceTile)
            {
                return;
            }

            SendGameCommand(new GameCommand
            {
                Type = GameCommandType.SelectTeleportDestination,
                TileIndex = destination
            });
        }
        private void BoardViewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isDraggingBoard)
                return;

            Point point = e.GetPosition(BoardViewport);
            if (isTeleportClickCandidate &&
                (Math.Abs(point.X - dragStart.X) > 5 || Math.Abs(point.Y - dragStart.Y) > 5))
            {
                isTeleportClickCandidate = false;
            }

            boardTranslation.X = dragTranslationX + point.X - dragStart.X;
            boardTranslation.Y = dragTranslationY + point.Y - dragStart.Y;
        }

        private void BoardViewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!isDraggingBoard)
                return;

            Point releasePoint = e.GetPosition(BoardViewport);
            bool selectTile = pendingTeleportPlayer.HasValue &&
                isTeleportClickCandidate &&
                Math.Abs(releasePoint.X - dragStart.X) <= 5 &&
                Math.Abs(releasePoint.Y - dragStart.Y) <= 5;

            isDraggingBoard = false;
            isTeleportClickCandidate = false;
            BoardViewport.ReleaseMouseCapture();
            BoardViewport.Cursor = pendingTeleportPlayer.HasValue
                ? Cursors.Cross
                : Cursors.Hand;

            if (selectTile)
            {
                int destination = GetTileIndexAtViewportPoint(releasePoint);
                if (destination >= 0 && destination != pendingTeleportSourceTile)
                    QueueTeleportDestination(destination);
            }

            e.Handled = true;
        }

        private void BoardViewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            Point pointer = e.GetPosition(BoardViewport);
            double oldScale = boardScale.ScaleX;

            double newScale = Math.Clamp(
                oldScale * (e.Delta > 0 ? 1.12 : 1 / 1.12),
                MinimumZoom,
                MaximumZoom);

            if (Math.Abs(newScale - oldScale) < 0.0001)
                return;

            StopCameraAnimation();

            double contentX = (pointer.X - boardTranslation.X) / oldScale;
            double contentY = (pointer.Y - boardTranslation.Y) / oldScale;

            boardScale.ScaleX = newScale;
            boardScale.ScaleY = newScale;

            boardTranslation.X = pointer.X - contentX * newScale;
            boardTranslation.Y = pointer.Y - contentY * newScale;

            e.Handled = true;
        }

        private void StopCameraAnimation()
        {
            double x = boardTranslation.X;
            double y = boardTranslation.Y;

            boardTranslation.BeginAnimation(TranslateTransform.XProperty, null);
            boardTranslation.BeginAnimation(TranslateTransform.YProperty, null);

            boardTranslation.X = x;
            boardTranslation.Y = y;
        }

        private void UpdatePlayerStatus()
        {
            PlayerStatusPanel.Children.Clear();

            if (gameState == null || turnOrder.Count == 0 ||
                !playerHP.TryGetValue(gameState.CurrentPlayerId, out int hp))
                return;

            int player = gameState.CurrentPlayerId;
            Border border = new()
            {
                Background = new SolidColorBrush(Color.FromRgb(39, 23, 28)),
                BorderBrush = GetPlayerColor(player),
                BorderThickness = new Thickness(2),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 2, 0, 2)
            };

            StackPanel panel = new();
            panel.Children.Add(new TextBlock
            {
                Text = $"PLAYER {player}",
                Foreground = GetPlayerColor(player),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 16,
                FontWeight = FontWeights.Bold
            });
            panel.Children.Add(new ProgressBar
            {
                Minimum = 0,
                Maximum = MaxHP,
                Value = hp,
                Height = 11,
                Margin = new Thickness(0, 4, 0, 3),
                Foreground = GetPlayerColor(player)
            });
            panel.Children.Add(new TextBlock
            {
                Text = hp > 0 ? $"HP {hp} / {MaxHP}" : "ELIMINATED",
                Foreground = Brushes.Bisque,
                FontSize = 12
            });
            panel.Children.Add(new TextBlock
            {
                Text = $"TILE {playerPositions[player]}  |  " +
                       $"ACTIVE CARDS {playerCards[player].Count}/3" +
                       (prisonTurns[player] > 0 ? "  |  PRISON" : ""),
                Foreground = Brushes.LightGray,
                FontSize = 11
            });

            border.Child = panel;
            PlayerStatusPanel.Children.Add(border);
        }

        private void UpdateTurnDisplay()
        {
            if (gameState == null || gameEnded || turnOrder.Count == 0)
                return;

            int player = gameState.CurrentPlayerId;

            CurrentTurnText.Text = $"PLAYER {player}";
            CurrentTurnText.Foreground = GetPlayerColor(player);
            RoundText.Text = $"ROUND {currentRound:00} / {gameState.MaximumRound}";

            bool suddenDeath = gameState.IsSuddenDeath;
            SuddenDeathText.Text = suddenDeath ? "SUDDEN DEATH" : "NORMAL MODE";
            SuddenDeathText.Foreground = suddenDeath
                ? Brushes.OrangeRed
                : Brushes.Bisque;

            RefreshHand();
        }

        private void CreateDiceChoiceButtons()
        {
            DiceChoicePanel.Children.Clear();
            DiceChoicePanel.RowDefinitions.Clear();
            DiceChoicePanel.ColumnDefinitions.Clear();

            DiceChoicePanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            DiceChoicePanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            for (int column = 0; column < 3; column++)
                DiceChoicePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            for (int number = 1; number <= 6; number++)
            {
                StackPanel label = new()
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                label.Children.Add(new TextBlock
                {
                    Text = number.ToString(),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 16,
                    FontWeight = FontWeights.Black,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                label.Children.Add(new TextBlock
                {
                    Text = "",
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 9,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Tag = number
                });

                Button choice = new()
                {
                    Content = label,
                    Tag = number,
                    Margin = new Thickness(2),
                    Padding = new Thickness(2),
                    Background = new SolidColorBrush(Color.FromRgb(54, 31, 36)),
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(165, 83, 53)),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    Focusable = false
                };
                Grid.SetRow(choice, (number - 1) / 3);
                Grid.SetColumn(choice, (number - 1) % 3);
                DiceChoicePanel.Children.Add(choice);
            }

            UpdateDiceChoiceLabels();
        }

        private void UpdateDiceChoiceLabels()
        {
            if (DiceChoicePanel == null || gameState == null || turnOrder.Count == 0)
                return;

            double preferredChance = gameState.PreferredDiceChance;

            foreach (Button choice in DiceChoicePanel.Children.OfType<Button>())
            {
                int number = (int)choice.Tag;
                StackPanel label = (StackPanel)choice.Content;
                TextBlock probability = (TextBlock)label.Children[1];
                double chance = hoveredDiceChoice == null
                    ? 1.0 / 6
                    : number == hoveredDiceChoice
                        ? preferredChance
                        : (1 - preferredChance) / 5;
                probability.Text = $"{chance:P1}";

                choice.Background = number == hoveredDiceChoice
                    ? new SolidColorBrush(Color.FromRgb(165, 74, 35))
                    : new SolidColorBrush(Color.FromRgb(54, 31, 36));
                choice.BorderBrush = number == hoveredDiceChoice
                    ? Brushes.Gold
                    : new SolidColorBrush(Color.FromRgb(165, 83, 53));
                choice.BorderThickness = number == hoveredDiceChoice
                    ? new Thickness(2)
                    : new Thickness(1);
            }
        }

        private void RollControlHost_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!CanStartRoll())
                return;

            isRollPressActive = true;
            isChoosingDice = false;
            hoveredDiceChoice = null;
            rollHoldTimer.Start();
            Mouse.Capture(RollControlHost);
            e.Handled = true;
        }

        private void RollControlHost_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!isRollPressActive || !isChoosingDice)
                return;

            hoveredDiceChoice = GetDiceChoiceAt(e.GetPosition(DiceChoicePanel));
            UpdateDiceChoiceLabels();
        }

        private void RollControlHost_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!isRollPressActive)
                return;

            rollHoldTimer.Stop();
            int? selected = isChoosingDice
                ? GetDiceChoiceAt(e.GetPosition(DiceChoicePanel))
                : null;
            bool wasChoosing = isChoosingDice;

            isRollPressActive = false;
            ResetDiceChoiceUI();
            Mouse.Capture(null);
            e.Handled = true;

            if (!wasChoosing || selected.HasValue)
                StartDiceRoll(selected);
        }

        private void RollHoldTimer_Tick(object? sender, EventArgs e)
        {
            rollHoldTimer.Stop();
            if (!isRollPressActive || !CanStartRoll())
                return;

            isChoosingDice = true;
            RollDiceButton.Visibility = Visibility.Collapsed;
            DiceChoicePanel.Visibility = Visibility.Visible;
            DiceChoicePanel.UpdateLayout();
            hoveredDiceChoice = GetDiceChoiceAt(Mouse.GetPosition(DiceChoicePanel));
            UpdateDiceChoiceLabels();
        }

        private int? GetDiceChoiceAt(Point point)
        {
            if (DiceChoicePanel.ActualWidth <= 0 || DiceChoicePanel.ActualHeight <= 0 ||
                point.X < 0 || point.Y < 0 ||
                point.X >= DiceChoicePanel.ActualWidth ||
                point.Y >= DiceChoicePanel.ActualHeight)
            {
                return null;
            }

            int column = Math.Min(2, (int)(point.X / (DiceChoicePanel.ActualWidth / 3)));
            int row = Math.Min(1, (int)(point.Y / (DiceChoicePanel.ActualHeight / 2)));
            return row * 3 + column + 1;
        }

        private void ResetDiceChoiceUI()
        {
            isChoosingDice = false;
            hoveredDiceChoice = null;
            DiceChoicePanel.Visibility = Visibility.Collapsed;
            RollDiceButton.Visibility = Visibility.Visible;
        }

        private void RollDiceButton_Click(object sender, RoutedEventArgs e)
        {
            if (!isRollPressActive)
                StartDiceRoll(preferredNumber: null);
        }

        private bool CanStartRoll()
        {
            return gameState != null &&
                   gameState.IsGameRunning &&
                   !gameState.IsMoving &&
                   !gameState.HasRolledThisTurn &&
                   !gameState.IsWaitingForPath &&
                   !gameState.IsWaitingForTeleport &&
                   !gameState.IsWaitingForCardTarget &&
                   !isRolling &&
                   !isClosing &&
                   RollDiceButton.IsEnabled &&
                   playerHP.TryGetValue(gameState.CurrentPlayerId, out int hp) && hp > 0;
        }

        private void StartDiceRoll(int? preferredNumber)
        {
            if (!CanStartRoll())
                return;

            isRolling = true;
            RollDiceButton.IsEnabled = false;
            DiceText.Text = "…";
            RefreshHand();
            SendGameCommand(new GameCommand
            {
                Type = GameCommandType.RollDice,
                PreferredDiceNumber = preferredNumber
            });
        }

        private void GameDice_RollCompleted(object? sender, int result)
        {
            if (gameEnded || isClosing || !isRolling)
                return;

            isRolling = false;
            DiceText.Text = result.ToString();
            StartMove(gameState?.CurrentPlayerId ?? movingPlayer);
            RefreshHand();
        }

        private void StartMove(int player)
        {
            if (gameEnded || isClosing)
                return;

            movingPlayer = player;
            moveTimer?.Stop();
            if (characters.TryGetValue(player, out PixelCharacter? character))
                character.StartWalking();

            moveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(430)
            };
            moveTimer.Tick += MoveTimer_Tick;
            moveTimer.Start();
        }

        private void MoveTimer_Tick(object? sender, EventArgs e)
        {
            if (gameEnded || isClosing || gameState?.IsMoving != true)
                return;

            SendGameCommand(new GameCommand { Type = GameCommandType.AdvanceMovement });
        }

        private void ShowPathChoices(IReadOnlyList<int> optionIndices)
        {
            moveTimer?.Stop();
            PathChoiceButtons.Children.Clear();

            for (int option = 0; option < optionIndices.Count; option++)
            {
                int pathIndex = option;
                int tileIndex = optionIndices[option];
                if (tileIndex < 0 || tileIndex >= stage.Map.Tiles.Count)
                    continue;

                TileData choice = stage.Map.Tiles[tileIndex];
                string tileName = GetTileName(choice);
                string label = string.IsNullOrWhiteSpace(tileName)
                    ? $"{choice.Position.X}, {choice.Position.Y}"
                    : $"{tileName} ({choice.Position.X}, {choice.Position.Y})";

                Button button = new()
                {
                    Content = $"{option + 1}. {label}",
                    Background = new SolidColorBrush(Color.FromRgb(83, 49, 94)),
                    Foreground = Brushes.White,
                    BorderBrush = Brushes.PeachPuff,
                    BorderThickness = new Thickness(2),
                    Padding = new Thickness(10, 7, 10, 7),
                    Margin = new Thickness(4),
                    FontFamily = new FontFamily("Consolas"),
                    FontWeight = FontWeights.Bold,
                    Cursor = Cursors.Hand
                };
                button.Click += (_, _) =>
                {
                    PathChoiceOverlay.Visibility = Visibility.Collapsed;
                    SendGameCommand(new GameCommand
                    {
                        Type = GameCommandType.SelectPath,
                        Index = pathIndex
                    });
                    moveTimer?.Start();
                };
                PathChoiceButtons.Children.Add(button);
            }

            PathChoiceOverlay.Visibility = Visibility.Visible;
        }
        private string GetTileName(TileData tile)
        {
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

        private void ShowHPChangeText(int player, int amount)
        {
            if (amount == 0 || isClosing || !characters.ContainsKey(player))
                return;

            int position = playerPositions[player];

            if (position < 0 || position >= tilePositions.Count)
                return;

            Point point = GetCharacterPosition(player, position);

            TextBlock effectText = new()
            {
                Text = amount > 0 ? $"+{amount} HP" : $"{amount} HP",
                Foreground = amount > 0 ? Brushes.LimeGreen : Brushes.OrangeRed,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 20,
                FontWeight = FontWeights.Black,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 5,
                    ShadowDepth = 2
                },
                IsHitTestVisible = false
            };

            Canvas.SetLeft(effectText, point.X - 5);
            Canvas.SetTop(effectText, point.Y - 15);
            Panel.SetZIndex(effectText, 200);
            BoardCanvas.Children.Add(effectText);

            DoubleAnimation rise = new(
                point.Y - 15,
                point.Y - 50,
                TimeSpan.FromMilliseconds(900));

            DoubleAnimation fade = new(
                1,
                0,
                TimeSpan.FromMilliseconds(900));

            fade.Completed += (_, _) => BoardCanvas.Children.Remove(effectText);

            effectText.BeginAnimation(Canvas.TopProperty, rise);
            effectText.BeginAnimation(OpacityProperty, fade);
        }

        private void AddGameLog(string message)
        {
            TextBlock log = new()
            {
                Text = $"[{DateTime.Now:HH:mm:ss}] {message}",
                Foreground = Brushes.Bisque,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                Margin = new Thickness(0, 2, 0, 2)
            };

            GameLogPanel.Children.Add(log);
            LogScrollViewer.ScrollToEnd();
        }

        private void RefreshHand()
        {
            if (HandPanel == null ||
                turnOrder == null ||
                playerCards.Count != playerCount)
                return;

            HandPanel.Children.Clear();

            if (turnOrder.Count == 0 || isClosing)
                return;

            int activePlayer = turnOrder[turnIndex];
            double availableWidth = HandScrollViewer?.ViewportWidth ?? 0;
            if (availableWidth <= 1)
                availableWidth = HandScrollViewer?.ActualWidth ?? 0;
            if (availableWidth <= 1)
                availableWidth = ActualWidth * 0.56;

            double playerPanelWidth = Math.Max(
                150,
                (availableWidth - 8 * playerCount) / playerCount);

            for (int player = 1; player <= playerCount; player++)
            {
                StackPanel playerPanel = new()
                {
                    Orientation = Orientation.Vertical
                };

                Border playerBorder = new()
                {
                    Background = new SolidColorBrush(Color.FromRgb(39, 23, 28)),
                    BorderBrush = player == activePlayer ? GetPlayerColor(player) : Brushes.SaddleBrown,
                    BorderThickness = new Thickness(player == activePlayer ? 2 : 1),
                    Padding = new Thickness(7),
                    Margin = new Thickness(3, 2, 3, 2),
                    Width = playerPanelWidth,
                    Child = playerPanel
                };

                TextBlock playerLabel = new()
                {
                    Text = $"PLAYER {player}  ·  {playerCards[player].Count}/3",
                    Foreground = GetPlayerColor(player),
                    FontFamily = new FontFamily("Consolas"),
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(2, 0, 0, 5)
                };
                playerPanel.Children.Add(playerLabel);

                StackPanel cardList = new()
                {
                    Orientation = Orientation.Vertical
                };
                playerPanel.Children.Add(cardList);

                if (playerCards[player].Count == 0)
                {
                    cardList.Children.Add(new TextBlock
                    {
                        Text = "—",
                        Foreground = Brushes.Gray,
                        Margin = new Thickness(3, 2, 0, 2)
                    });
                }

                foreach (CardData card in playerCards[player].ToArray())
                {
                    StackPanel tooltipContent = new();
                    tooltipContent.Children.Add(new TextBlock
                    {
                        Text = card.Name,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                        TextWrapping = TextWrapping.Wrap
                    });
                    tooltipContent.Children.Add(new TextBlock
                    {
                        Text = card.Description,
                        Foreground = Brushes.Bisque,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 4, 0, 0)
                    });

                    ToolTip toolTip = new()
                    {
                        Content = tooltipContent,
                        Placement = PlacementMode.Mouse,
                        MaxWidth = 280,
                        Background = new SolidColorBrush(Color.FromRgb(25, 13, 17)),
                        BorderBrush = Brushes.Goldenrod,
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(8)
                    };
                    Button button = new()
                    {
                        Content = new TextBlock
                        {
                            Text = card.Name,
                            TextWrapping = TextWrapping.NoWrap,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            TextAlignment = TextAlignment.Center
                        },
                        ToolTip = toolTip,
                        Width = Math.Max(112, playerPanelWidth - 24),
                        Height = 34,
                        Margin = new Thickness(2, 2, 2, 3),
                        Padding = new Thickness(3, 1, 3, 1),
                        Background = new SolidColorBrush(Color.FromRgb(64, 43, 96)),
                        BorderBrush = Brushes.Goldenrod,
                        Foreground = Brushes.Bisque,
                        FontSize = 12,
                        FontWeight = FontWeights.Bold,
                        IsEnabled = !gameEnded &&
                                    player == activePlayer &&
                                    !hasRolledThisTurn &&
                                    !isMoving &&
                                    !isRolling &&
                                    gameState?.IsWaitingForCardTarget != true &&
                                    !isClosing &&
                                    card.Usable
                    };

                    button.Click += (_, _) => UseCard(card);
                    ToolTipService.SetShowOnDisabled(button, true);
                    ToolTipService.SetPlacement(button, PlacementMode.Mouse);
                    ToolTipService.SetInitialShowDelay(button, 100);
                    cardList.Children.Add(button);
                }

                HandPanel.Children.Add(playerBorder);
            }
        }

        private void HandScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RefreshHand();
        }

        private void UseCard(CardData card)
        {
            if (gameState == null || !CanStartRoll())
                return;

            int owner = gameState.CurrentPlayerId;
            if (!playerCards.TryGetValue(owner, out List<CardData>? cards))
                return;

            int cardIndex = cards.FindIndex(candidate => ReferenceEquals(candidate, card));
            if (cardIndex < 0 || !card.Usable)
                return;

            SendGameCommand(new GameCommand
            {
                Type = GameCommandType.UseCard,
                Index = cardIndex
            });
        }

        private void ShowCardTargetSelection(GameManagerMessage message)
        {
            pendingTargetCardIndex = message.CardIndex;
            pendingTargetOwner = message.PlayerId;
            pendingTargetIsImmediate = message.IsImmediateCard;
            CardTargetText.Text = $"PLAYER {message.PlayerId} · {message.Text}\n아래 플레이어 지정 칸에서 대상을 선택하세요.";
            CardTargetButtons.Children.Clear();

            foreach (int target in message.OptionIndices)
            {
                if (!playerHP.TryGetValue(target, out int hp) || hp <= 0)
                    continue;

                Button targetButton = new()
                {
                    Content = $"PLAYER {target}\nHP {hp} / {MaxHP}",
                    Width = 125,
                    Height = 48,
                    Margin = new Thickness(3),
                    Padding = new Thickness(4),
                    Background = new SolidColorBrush(Color.FromRgb(39, 23, 28)),
                    BorderBrush = GetPlayerColor(target),
                    BorderThickness = new Thickness(3),
                    Foreground = Brushes.Bisque,
                    FontFamily = new FontFamily("Consolas"),
                    FontWeight = FontWeights.Bold,
                    Cursor = Cursors.Hand
                };
                targetButton.Click += (_, _) => QueueCardTargetSelection(
                    target,
                    message.CardIndex,
                    message.PlayerId,
                    message.IsImmediateCard);
                CardTargetButtons.Children.Add(targetButton);
            }

            CancelTargetButton.Visibility = message.IsImmediateCard
                ? Visibility.Collapsed
                : Visibility.Visible;
            CardTargetOverlay.Visibility = Visibility.Visible;
        }

        private void CancelTargetButton_Click(object sender, RoutedEventArgs e)
        {
            if (gameState?.IsWaitingForCardTarget != true || pendingTargetIsImmediate)
                return;

            SendGameCommand(new GameCommand { Type = GameCommandType.CancelCardTarget });
        }

        private void UpdateRollButtonState()
        {
            if (gameState == null ||
                gameEnded || isClosing || isRolling ||
                gameState.IsMoving || gameState.HasRolledThisTurn ||
                gameState.IsWaitingForPath || gameState.IsWaitingForTeleport ||
                gameState.IsWaitingForCardTarget)
            {
                RollDiceButton.IsEnabled = false;
                return;
            }

            RollDiceButton.IsEnabled = playerHP.TryGetValue(gameState.CurrentPlayerId, out int hp) && hp > 0;
        }
        public List<Player> ResultPlayers()
        {
            return playerHP.Select(entry =>
            {
                Player result = new(
                    entry.Key,
                    $"PLAYER {entry.Key}",
                    new TileData());

                result.TakeDamage(MaxHP - entry.Value);
                return result;
            }).ToList();
        }

        private void StopAnimations()
        {
            moveTimer?.Stop();
            rollHoldTimer.Stop();
            isRollPressActive = false;
            ResetDiceChoiceUI();
            pendingTeleportPlayer = null;
            pendingTeleportSourceTile = -1;
            TeleportTargetOverlay.Visibility = Visibility.Collapsed;
            if (RollControlHost.IsMouseCaptured)
                Mouse.Capture(null);
            GameDice.StopRolling();
            StopCameraAnimation();

            isMoving = false;
            isRolling = false;

            foreach (PixelCharacter character in characters.Values)
                character.StopAllAnimations();
        }

        private void DisposeGameConnections()
        {
            if (connectionsDisposed)
                return;

            connectionsDisposed = true;
            GameDice.RollCompleted -= GameDice_RollCompleted;
            gameConnector.OnInvoke = null;
            gameConnector.Destroy();
            gameManager.Dispose();
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            if (isClosing)
                return;

            isClosing = true;
            StopAnimations();
            DisposeGameConnections();
            ExitRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
