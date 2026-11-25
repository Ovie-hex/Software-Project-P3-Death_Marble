using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
        private readonly Random random = new();
        private readonly int playerCount;
        private readonly List<int> turnOrder;
        private readonly StageData stage;

        private readonly Dictionary<int, PixelCharacter> characters = new();
        private readonly Dictionary<int, int> playerPositions = new();
        private readonly Dictionary<int, int> playerHP = new();
        private readonly Dictionary<int, List<CardData>> playerCards = new();
        private readonly Dictionary<int, int> preferredDice = new();
        private readonly Dictionary<int, int> prisonTurns = new();
        private readonly List<Point> tilePositions = new();

        private readonly ScaleTransform boardScale = new(1, 1);
        private readonly TranslateTransform boardTranslation = new();
        private readonly TransformGroup boardTransform = new();

        private DispatcherTimer? moveTimer;

        private int turnIndex;
        private int currentRound = 1;
        private int remainingSteps;
        private int movingPlayer;

        private bool isMoving;
        private bool isRolling;
        private bool isClosing;
        private bool cardUsed;
        private bool gameEnded;
        private bool hasInitialBoardView;
        private bool isDraggingBoard;

        private Point dragStart;
        private double dragTranslationX;
        private double dragTranslationY;

        private const int MaxHP = 100;
        private const int MaxRounds = 40;
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
                },
                null)
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
                },
                null)
        {
        }

        public MainGame(int playerCount, List<int> turnOrder, StageData stage)
            : this(playerCount, turnOrder, stage, null)
        {
        }

        public MainGame(
            int playerCount,
            List<int> turnOrder,
            StageData stage,
            Dictionary<int, int>? preferences)
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

            if (stage.Map.Start == null)
                throw new ArgumentException("스테이지에 시작 타일이 없습니다.", nameof(stage));

            if (stage.Map.Tiles.Count == 0)
                stage.Map.Tiles.Add(stage.Map.Start);

            if (!stage.Map.Tiles.Any(tile => ReferenceEquals(tile, stage.Map.Start)))
                stage.Map.Tiles.Insert(0, stage.Map.Start);

            foreach (int id in this.turnOrder)
            {
                preferredDice[id] =
                    preferences != null && preferences.TryGetValue(id, out int value)
                        ? Math.Clamp(value, 1, 6)
                        : 1;
            }

            GameDice.RollCompleted += GameDice_RollCompleted;

            boardTransform.Children.Add(boardScale);
            boardTransform.Children.Add(boardTranslation);
            BoardCanvas.RenderTransform = boardTransform;

            Loaded += (_, _) => FitBoardToViewport();

            InitializePlayers();
            CreateBoard();
            CreateCharacters();
            UpdatePlayerStatus();
            UpdateTurnDisplay();

            AddGameLog("DEATH MARBLE 게임 시작!");
            AddGameLog("턴 순서: " + string.Join(" → ", turnOrder.Select(p => $"P{p}")));
        }

        private void InitializePlayers()
        {
            int startIndex = GetTileIndex(stage.Map.Start!);

            for (int player = 1; player <= playerCount; player++)
            {
                playerPositions[player] = startIndex;
                playerHP[player] = MaxHP;
                playerCards[player] = new List<CardData>();
                prisonTurns[player] = 0;

                CardData? startingCard = DrawRandomCard();
                if (startingCard != null)
                    playerCards[player].Add(startingCard);
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
                _ => (new SolidColorBrush(Color.FromRgb(53, 35, 41)), "ZONE")
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

            TextBlock text = new TextBlock
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
            };

            Grid grid = new Grid();
            grid.Children.Add(text);
            border.Child = grid;
            border.ToolTip = $"{label} · ({tile.Position.X}, {tile.Position.Y})";

            return border;
        }

        private void CreateCharacters()
        {
            characters.Clear();

            for (int player = 1; player <= playerCount; player++)
            {
                PixelCharacter character = new(player);
                characters[player] = character;
                Panel.SetZIndex(character, 100);
                BoardCanvas.Children.Add(character);
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

            Canvas.SetLeft(character, to.X);
            Canvas.SetTop(character, to.Y);

            Duration duration = TimeSpan.FromMilliseconds(380);
            CubicEase easing = new() { EasingMode = EasingMode.EaseOut };

            character.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation
            {
                From = from.X,
                To = to.X,
                Duration = duration,
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop
            });

            character.BeginAnimation(Canvas.TopProperty, new DoubleAnimation
            {
                From = from.Y,
                To = to.Y,
                Duration = duration,
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop
            });

            FocusCameraOnTile(tileIndex, duration);
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
            StopCameraAnimation();
            isDraggingBoard = true;
            dragStart = e.GetPosition(BoardViewport);
            dragTranslationX = boardTranslation.X;
            dragTranslationY = boardTranslation.Y;
            BoardViewport.CaptureMouse();
            BoardViewport.Cursor = Cursors.SizeAll;
            e.Handled = true;
        }

        private void BoardViewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isDraggingBoard)
                return;

            Point point = e.GetPosition(BoardViewport);
            boardTranslation.X = dragTranslationX + point.X - dragStart.X;
            boardTranslation.Y = dragTranslationY + point.Y - dragStart.Y;
        }

        private void BoardViewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!isDraggingBoard)
                return;

            isDraggingBoard = false;
            BoardViewport.ReleaseMouseCapture();
            BoardViewport.Cursor = Cursors.Hand;
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

            for (int player = 1; player <= playerCount; player++)
            {
                int hp = playerHP[player];

                Border border = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(39, 23, 28)),
                    BorderBrush = GetPlayerColor(player),
                    BorderThickness = new Thickness(2),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 4, 0, 4)
                };

                StackPanel panel = new StackPanel();

                panel.Children.Add(new TextBlock
                {
                    Text = $"PLAYER {player}",
                    Foreground = GetPlayerColor(player),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 17,
                    FontWeight = FontWeights.Bold
                });

                panel.Children.Add(new ProgressBar
                {
                    Minimum = 0,
                    Maximum = MaxHP,
                    Value = hp,
                    Height = 13,
                    Margin = new Thickness(0, 8, 0, 5)
                });

                panel.Children.Add(new TextBlock
                {
                    Text = hp > 0 ? $"HP {hp} / {MaxHP}" : "ELIMINATED",
                    Foreground = Brushes.Bisque,
                    FontSize = 13
                });

                panel.Children.Add(new TextBlock
                {
                    Text = $"TILE {playerPositions[player]}  |  " +
                           $"CARD {playerCards[player].Count}  |  " +
                           $"PICK {preferredDice[player]}" +
                           (prisonTurns[player] > 0 ? "  |  PRISON" : ""),
                    Foreground = Brushes.LightGray,
                    FontSize = 12
                });

                border.Child = panel;
                PlayerStatusPanel.Children.Add(border);
            }
        }

        private void UpdateTurnDisplay()
        {
            if (gameEnded)
                return;

            int player = turnOrder[turnIndex];

            CurrentTurnText.Text = $"PLAYER {player}";
            CurrentTurnText.Foreground = GetPlayerColor(player);
            RoundText.Text = $"ROUND {currentRound:00} / {MaxRounds}";

            bool suddenDeath = currentRound >= 20;
            SuddenDeathText.Text = suddenDeath ? "SUDDEN DEATH" : "NORMAL MODE";
            SuddenDeathText.Foreground = suddenDeath
                ? Brushes.OrangeRed
                : Brushes.Bisque;

            RefreshHand();
        }

        private void RollDiceButton_Click(object sender, RoutedEventArgs e)
        {
            if (isMoving || isRolling || gameEnded || isClosing)
                return;

            int player = turnOrder[turnIndex];

            if (playerHP[player] <= 0)
                return;

            isRolling = true;
            RollDiceButton.IsEnabled = false;
            DiceText.Text = "ROLLING...";
            RefreshHand();

            int result = DiceProbability.Roll(
                random,
                preferredDice[player],
                playerHP[player],
                MaxHP);

            AddGameLog($"PLAYER {player}: 주사위 굴리는 중...");
            GameDice.Roll(result);
        }

        private void GameDice_RollCompleted(object? sender, int result)
        {
            if (gameEnded || isClosing || !isRolling)
                return;

            isRolling = false;
            DiceText.Text = result.ToString();

            int player = turnOrder[turnIndex];
            AddGameLog($"PLAYER {player}: 주사위 {result}");

            StartMove(player, result);
            RefreshHand();
        }

        private void StartMove(int player, int diceValue)
        {
            if (gameEnded || isClosing)
                return;

            isMoving = true;
            movingPlayer = player;
            remainingSteps = diceValue;
            RollDiceButton.IsEnabled = false;

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
            if (gameEnded || isClosing || !isMoving)
                return;

            if (remainingSteps <= 0)
            {
                FinishMove();
                return;
            }

            int currentPosition = playerPositions[movingPlayer];
            TileData currentTile = stage.Map.Tiles[currentPosition];

            if (currentTile.Next.Count > 1)
            {
                ShowPathChoices(currentTile.Next);
                return;
            }

            TileData nextTile = currentTile.Next.FirstOrDefault()
                ?? stage.Map.Start!;

            AdvancePlayerTo(nextTile);
        }

        private void ShowPathChoices(IReadOnlyList<TileData> choices)
        {
            moveTimer?.Stop();
            PathChoiceButtons.Children.Clear();

            for (int i = 0; i < choices.Count; i++)
            {
                TileData choice = choices[i];

                Button button = new()
                {
                    Content = $"{i + 1}. {GetTileName(choice)} " +
                              $"({choice.Position.X}, {choice.Position.Y})",
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
                    AdvancePlayerTo(choice);
                    moveTimer?.Start();
                };

                PathChoiceButtons.Children.Add(button);
            }

            PathChoiceOverlay.Visibility = Visibility.Visible;
        }

        private void AdvancePlayerTo(TileData nextTile)
        {
            int nextPosition = GetTileIndex(nextTile);

            if (nextPosition < 0)
                nextPosition = GetTileIndex(stage.Map.Start!);

            playerPositions[movingPlayer] = nextPosition;
            remainingSteps--;

            AnimateCharacterToTile(movingPlayer, nextPosition);
            UpdatePlayerStatus();
        }

        private void FinishMove()
        {
            moveTimer?.Stop();
            isMoving = false;

            if (characters.TryGetValue(movingPlayer, out PixelCharacter? character))
                character.StopWalking();

            int position = playerPositions[movingPlayer];
            TileData tile = stage.Map.Tiles[position];

            AddGameLog(
                $"PLAYER {movingPlayer}: {GetTileName(tile)} 타일 도착");

            ApplyTileEvent(movingPlayer, tile);
            UpdatePlayerStatus();

            if (CheckGameEnd())
                return;

            NextTurn();
        }

        private string GetTileName(TileData tile)
        {
            if (ReferenceEquals(tile, stage.Map.Start))
                return "START";

            return tile.Type switch
            {
                TileType.Damage => "DAMAGE",
                TileType.Heal => "HEAL",
                TileType.Card => "CARD",
                TileType.Prison => "PRISON",
                TileType.Teleport => "TELEPORT",
                TileType.Hole => "HOLE",
                _ => "ZONE"
            };
        }

        private void ApplyTileEvent(int player, TileData tile)
        {
            if (ReferenceEquals(tile, stage.Map.Start))
            {
                AddGameLog($"PLAYER {player}: 시작 타일에 도착했습니다.");
            }
            else
            {
                switch (tile.Type)
                {
                    case TileType.Card:
                        if (playerCards[player].Count >= 3)
                        {
                            AddGameLog($"PLAYER {player}: 카드 보유 한도 3장");
                        }
                        else
                        {
                            CardData? card = DrawRandomCard();

                            if (card == null)
                                AddGameLog("이 스테이지에는 획득할 카드가 없습니다.");
                            else
                            {
                                playerCards[player].Add(card);
                                AddGameLog($"PLAYER {player}: 카드 획득 — {card.Name}");
                            }
                        }
                        break;

                    case TileType.Heal:
                        ChangeHP(player, tile.Amount);
                        break;

                    case TileType.Damage:
                    case TileType.Hole:
                        ChangeHP(player, -tile.Amount);
                        break;

                    case TileType.Prison:
                        prisonTurns[player] = Math.Max(prisonTurns[player], 1);
                        AddGameLog($"PLAYER {player}: 감옥에 갇혀 다음 턴을 쉽니다.");
                        break;

                    case TileType.Teleport:
                        if (stage.Map.Tiles.Count > 1)
                        {
                            int destination = GetRandomOtherTile(player);
                            RelocatePlayer(player, destination);
                            FocusCameraOnTile(
                                destination,
                                TimeSpan.FromMilliseconds(450));
                            AddGameLog($"PLAYER {player}: 텔레포트 — 타일 {destination}");
                        }
                        break;
                }
            }

            // main의 서든 데스 규칙: 20라운드부터 도착 시 HP -5
            if (currentRound >= 20 && playerHP[player] > 0)
            {
                ChangeHP(player, -5);
                AddGameLog($"PLAYER {player}: 서든데스 HP -5");
            }
        }

        private int GetRandomOtherTile(int player)
        {
            int current = playerPositions[player];

            List<int> destinations = Enumerable
                .Range(0, stage.Map.Tiles.Count)
                .Where(index => index != current)
                .ToList();

            return destinations.Count == 0
                ? current
                : destinations[random.Next(destinations.Count)];
        }

        private void RelocatePlayer(int player, int tileIndex)
        {
            if (tileIndex < 0 || tileIndex >= stage.Map.Tiles.Count)
                return;

            playerPositions[player] = tileIndex;
            UpdateCharacterPosition(player);
        }

        private void ChangeHP(int player, int delta)
        {
            if (!playerHP.ContainsKey(player) ||
                playerHP[player] <= 0 ||
                delta == 0)
            {
                return;
            }

            int oldHP = playerHP[player];
            playerHP[player] = Math.Clamp(oldHP + delta, 0, MaxHP);

            int actualChange = playerHP[player] - oldHP;
            if (actualChange == 0)
                return;

            if (characters.TryGetValue(player, out PixelCharacter? character))
            {
                if (actualChange < 0)
                    character.PlayDamageEffect();
                else
                    character.PlayHealEffect();
            }

            ShowHPChangeText(player, actualChange);
            AddGameLog($"PLAYER {player}: HP {actualChange:+0;-0}");

            if (playerHP[player] == 0 &&
                characters.TryGetValue(player, out PixelCharacter? eliminated))
            {
                eliminated.Visibility = Visibility.Collapsed;
                AddGameLog($"PLAYER {player} 탈락!");
            }
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

        private void NextTurn()
        {
            if (CheckGameEnd())
                return;

            while (!gameEnded)
            {
                for (int i = 0; i < turnOrder.Count; i++)
                {
                    turnIndex++;

                    if (turnIndex >= turnOrder.Count)
                    {
                        turnIndex = 0;
                        currentRound++;
                    }

                    if (currentRound > MaxRounds)
                    {
                        EndGameByHP();
                        return;
                    }

                    int player = turnOrder[turnIndex];

                    if (playerHP[player] <= 0)
                        continue;

                    if (prisonTurns[player] > 0)
                    {
                        prisonTurns[player]--;
                        AddGameLog($"PLAYER {player}: 감옥으로 턴을 건너뜁니다.");
                        continue;
                    }

                    cardUsed = false;
                    DiceText.Text = "READY";
                    RollDiceButton.IsEnabled = true;

                    UpdateTurnDisplay();
                    UpdatePlayerStatus();
                    AddGameLog($"PLAYER {player} 턴 시작");
                    return;
                }
            }
        }

        private bool CheckGameEnd()
        {
            List<int> alivePlayers = playerHP
                .Where(entry => entry.Value > 0)
                .Select(entry => entry.Key)
                .ToList();

            if (alivePlayers.Count > 1)
                return false;

            EndGame(alivePlayers.Count == 1 ? alivePlayers[0] : null);
            return true;
        }

        private void EndGameByHP()
        {
            if (playerHP.Count == 0)
            {
                EndGame(null);
                return;
            }

            int maxHP = playerHP.Values.Max();

            List<int> leaders = playerHP
                .Where(entry => entry.Value == maxHP)
                .Select(entry => entry.Key)
                .ToList();

            EndGame(leaders.Count == 1 ? leaders[0] : null);
        }

        private void EndGame(int? winner)
        {
            if (gameEnded)
                return;

            gameEnded = true;
            StopAnimations();
            RollDiceButton.IsEnabled = false;
            RefreshHand();

            AddGameLog(
                winner.HasValue
                    ? $"PLAYER {winner.Value} 승리!"
                    : "게임 종료: 공동 결과");

            GameFinished?.Invoke(this, winner);
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

        private CardData? DrawRandomCard()
        {
            long total = stage.Cards
                .Where(entry => entry != null &&
                                entry.Card != null &&
                                entry.Count > 0)
                .Sum(entry => (long)entry.Count);

            if (total <= 0)
                return null;

            long ticket = random.NextInt64(total);

            foreach (CardEntry entry in stage.Cards)
            {
                if (entry == null || entry.Card == null || entry.Count <= 0)
                    continue;

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

        private void RefreshHand()
        {
            if (HandPanel == null)
                return;

            HandPanel.Children.Clear();

            if (turnOrder.Count == 0 || gameEnded || isClosing)
                return;

            int player = turnOrder[turnIndex];

            foreach (CardData card in playerCards[player].ToArray())
            {
                TextBlock label = new()
                {
                    Text = $"{card.Name}\n{card.Description}",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brushes.Bisque
                };

                Button button = new()
                {
                    Content = label,
                    Width = 155,
                    Height = 78,
                    Margin = new Thickness(4),
                    Padding = new Thickness(7),
                    Background = Brushes.DarkSlateBlue,
                    BorderBrush = Brushes.Goldenrod,
                    IsEnabled = !cardUsed &&
                                !isMoving &&
                                !isRolling &&
                                !gameEnded &&
                                !isClosing &&
                                card.Usable
                };

                button.Click += (_, _) => UseCard(card);
                HandPanel.Children.Add(button);
            }
        }

        private void UseCard(CardData card)
        {
            if (cardUsed || isMoving || isRolling || gameEnded || isClosing)
                return;

            int player = turnOrder[turnIndex];

            if (!card.Usable || !playerCards[player].Contains(card))
                return;

            int? selectedOther = null;

            if (card.Effects.Any(effect =>
                    effect.Type != EffectType.None &&
                    effect.Target == TargetType.Other))
            {
                selectedOther = FateDialog.Choose(
                    Window.GetWindow(this),
                    "대상 선택",
                    turnOrder
                        .Where(id => id != player && playerHP[id] > 0)
                        .Select(id => (id, $"PLAYER {id} · HP {playerHP[id]}")));

                if (!selectedOther.HasValue)
                    return;
            }

            foreach (EffectData effect in card.Effects)
                ApplyCardEffect(player, effect, selectedOther);

            playerCards[player].Remove(card);
            cardUsed = true;

            AddGameLog($"PLAYER {player}: {card.Name} 사용");
            UpdatePlayerStatus();
            RefreshHand();

            if (!CheckGameEnd() && playerHP[player] <= 0)
                NextTurn();
        }

        private void ApplyCardEffect(
            int owner,
            EffectData effect,
            int? selectedOther)
        {
            if (effect.Type == EffectType.None)
                return;

            int repeat = Math.Clamp(effect.Count, 0, 50);

            for (int i = 0; i < repeat; i++)
            {
                if (effect.Type == EffectType.Swap &&
                    effect.Target == TargetType.All)
                {
                    ShuffleAlivePlayerPositions();
                    continue;
                }

                List<int> targets = ResolveCardTargets(
                    owner,
                    effect.Target,
                    selectedOther);

                foreach (int target in targets)
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
                            if (stage.Map.Tiles.Count > 1)
                            {
                                int destination = GetRandomOtherTile(target);
                                RelocatePlayer(target, destination);
                                FocusCameraOnTile(
                                    destination,
                                    TimeSpan.FromMilliseconds(450));
                            }
                            break;

                        case EffectType.Swap:
                            if (target != owner)
                                SwapPlayerPositions(owner, target);
                            break;
                    }
                }
            }
        }

        private List<int> ResolveCardTargets(
            int owner,
            TargetType target,
            int? selectedOther)
        {
            switch (target)
            {
                case TargetType.Self:
                    return playerHP[owner] > 0
                        ? new List<int> { owner }
                        : new List<int>();

                case TargetType.Other:
                    return selectedOther.HasValue &&
                           playerHP.TryGetValue(selectedOther.Value, out int hp) &&
                           hp > 0
                        ? new List<int> { selectedOther.Value }
                        : new List<int>();

                case TargetType.All:
                    return turnOrder
                        .Where(id => playerHP[id] > 0)
                        .ToList();

                case TargetType.Random:
                    List<int> opponents = turnOrder
                        .Where(id => id != owner && playerHP[id] > 0)
                        .ToList();

                    return opponents.Count == 0
                        ? new List<int>()
                        : new List<int>
                        {
                            opponents[random.Next(opponents.Count)]
                        };

                default:
                    return new List<int>();
            }
        }

        private void SwapPlayerPositions(int first, int second)
        {
            (playerPositions[first], playerPositions[second]) =
                (playerPositions[second], playerPositions[first]);

            UpdateCharacterPosition(first);
            UpdateCharacterPosition(second);
        }

        private void ShuffleAlivePlayerPositions()
        {
            List<int> players = turnOrder
                .Where(id => playerHP[id] > 0)
                .ToList();

            List<int> positions = players
                .Select(id => playerPositions[id])
                .ToList();

            for (int i = positions.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (positions[i], positions[j]) = (positions[j], positions[i]);
            }

            for (int i = 0; i < players.Count; i++)
            {
                playerPositions[players[i]] = positions[i];
                UpdateCharacterPosition(players[i]);
            }
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
            GameDice.StopRolling();
            StopCameraAnimation();

            isMoving = false;
            isRolling = false;

            foreach (PixelCharacter character in characters.Values)
                character.StopAllAnimations();
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            if (isClosing)
                return;

            isClosing = true;
            StopAnimations();
            ExitRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}