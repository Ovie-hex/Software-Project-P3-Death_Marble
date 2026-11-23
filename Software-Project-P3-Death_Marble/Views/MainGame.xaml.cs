using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Software_Project_P3_Death_Marble.Views
{
    public partial class MainGame : UserControl
    {
        // =====================================
        // 기본 설정
        // =====================================

        private readonly Random random = new Random();

        private readonly int playerCount;

        private readonly List<int> turnOrder;

        private readonly StageData stage;

        private readonly Dictionary<int, PixelCharacter> characters = new();

        private readonly Dictionary<int, int> playerPositions = new();

        private readonly Dictionary<int, int> playerHP = new();

        private readonly Dictionary<int, int> playerCards = new();

        private readonly List<Point> tilePositions = new();

        private readonly ScaleTransform boardScale = new(1, 1);

        private readonly TranslateTransform boardTranslation = new();

        private readonly TransformGroup boardTransform = new();

        private DispatcherTimer? moveTimer;

        private int turnIndex = 0;

        private int currentRound = 1;

        private int remainingSteps = 0;

        private int movingPlayer = 0;

        private bool isMoving = false;

        private bool gameEnded = false;

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

        // =====================================
        // 이벤트
        // =====================================

        public event EventHandler? ExitRequested;

        public event EventHandler<int?>? GameFinished;

        // =====================================
        // 생성자
        // =====================================

        public MainGame()
            : this(2, new List<int> { 1, 2 }, new StageData
            {
                Name = "기본 스테이지",
                Map = MapData.CreateDefault()
            })
        {
        }

        public MainGame(
            int playerCount,
            List<int> turnOrder)
            : this(playerCount, turnOrder, new StageData
            {
                Name = "기본 스테이지",
                Map = MapData.CreateDefault()
            })
        {
        }

        public MainGame(
            int playerCount,
            List<int> turnOrder,
            StageData stage)
        {
            InitializeComponent();

            if (playerCount < 2 || playerCount > 4)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(playerCount));
            }

            if (turnOrder == null ||
                turnOrder.Count != playerCount ||
                turnOrder.Distinct().Count() != playerCount ||
                turnOrder.Any(p => p < 1 || p > playerCount))
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

            AddGameLog(
                "턴 순서: " +
                string.Join(" → ",
                    turnOrder.Select(p => $"P{p}")));
        }

        // =====================================
        // 플레이어 초기화
        // =====================================

        private void InitializePlayers()
        {
            int startIndex = GetTileIndex(stage.Map.Start!);

            for (int i = 1; i <= playerCount; i++)
            {
                playerPositions[i] = startIndex;

                playerHP[i] = MaxHP;

                playerCards[i] = 0;
            }
        }

        // =====================================
        // 플레이어 색상
        // =====================================

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

        // =====================================
        // 게임판 생성
        // =====================================

        private void CreateBoard()
        {
            BoardCanvas.Children.Clear();

            tilePositions.Clear();

            List<TileData> mapTiles = stage.Map.Tiles;
            TileData startTile = stage.Map.Start!;
            double startX = startTile.Position.X;
            double startY = startTile.Position.Y;

            List<(double X, double Y)> normalized = mapTiles
                .Select(tile => ((double)tile.Position.X - startX,
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
                        nextIndex = mapTiles.FindIndex(tile =>
                            tile.Position == next.Position);

                    if (nextIndex < 0 || nextIndex == i)
                        continue;

                    (int From, int To) edge = i < nextIndex
                        ? (i, nextIndex)
                        : (nextIndex, i);
                    if (!drawnConnections.Add(edge))
                        continue;

                    Point from = tilePositions[i];
                    Point to = tilePositions[nextIndex];
                    BoardCanvas.Children.Add(new Line
                    {
                        X1 = from.X + TileSize / 2.0,
                        Y1 = from.Y + TileSize / 2.0,
                        X2 = to.X + TileSize / 2.0,
                        Y2 = to.Y + TileSize / 2.0,
                        Stroke = Brushes.DarkGoldenrod,
                        StrokeThickness = 7,
                        IsHitTestVisible = false
                    });
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

        // =====================================
        // 타일 생성
        // =====================================

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
                BorderThickness = new Thickness(isStart ? 4 : 3),
                CornerRadius = new CornerRadius(0)
            };

            TextBlock text = new TextBlock
            {
                Text = label,
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(3, 5, 3, 0),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center
            };

            Grid grid = new Grid();

            grid.Children.Add(text);

            border.Child = grid;

            border.ToolTip = $"{label} · ({tile.Position.X}, {tile.Position.Y})";

            return border;
        }

        // =====================================
        // 픽셀 캐릭터 생성
        // =====================================

        private void CreateCharacters()
        {
            characters.Clear();

            for (int player = 1; player <= playerCount; player++)
            {
                PixelCharacter character =
                    new PixelCharacter(player);

                characters[player] = character;

                Panel.SetZIndex(character, 100);

                BoardCanvas.Children.Add(character);
            }

            UpdateAllCharacterPositions();
        }

        // =====================================
        // 캐릭터 위치 갱신
        // =====================================

        private void UpdateAllCharacterPositions()
        {
            foreach (int player in characters.Keys)
            {
                UpdateCharacterPosition(player);
            }
        }

        private void UpdateCharacterPosition(int player)
        {
            if (!characters.TryGetValue(
                player,
                out PixelCharacter? character))
            {
                return;
            }

            if (!playerPositions.TryGetValue(
                player,
                out int position))
            {
                return;
            }

            if (position < 0 ||
                position >= tilePositions.Count)
            {
                return;
            }

            Point characterPoint = GetCharacterPosition(player, position);

            Canvas.SetLeft(
                character,
                characterPoint.X);

            Canvas.SetTop(
                character,
                characterPoint.Y);
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
            int index = stage.Map.Tiles.FindIndex(candidate =>
                ReferenceEquals(candidate, tile));
            if (index >= 0)
                return index;

            return stage.Map.Tiles.FindIndex(candidate =>
                candidate.Position == tile.Position);
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
                return;

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
            double targetX = BoardViewport.ActualWidth / 2 - center.X * boardScale.ScaleX;
            double targetY = BoardViewport.ActualHeight / 2 - center.Y * boardScale.ScaleY;
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

        // =====================================
        // 플레이어 상태 표시
        // =====================================

        private void UpdatePlayerStatus()
        {
            PlayerStatusPanel.Children.Clear();

            for (int player = 1; player <= playerCount; player++)
            {
                int hp = playerHP[player];

                Border border = new Border
                {
                    Background = new SolidColorBrush(
                        Color.FromRgb(39, 23, 28)),

                    BorderBrush = GetPlayerColor(player),

                    BorderThickness = new Thickness(2),

                    Padding = new Thickness(10),

                    Margin = new Thickness(0, 4, 0, 4)
                };

                StackPanel panel = new StackPanel();

                TextBlock name = new TextBlock
                {
                    Text = $"PLAYER {player}",
                    Foreground = GetPlayerColor(player),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 17,
                    FontWeight = FontWeights.Bold
                };

                ProgressBar hpBar = new ProgressBar
                {
                    Minimum = 0,
                    Maximum = MaxHP,
                    Value = hp,
                    Height = 13,
                    Margin = new Thickness(0, 8, 0, 5)
                };

                TextBlock hpText = new TextBlock
                {
                    Text = hp > 0
                        ? $"HP {hp} / {MaxHP}"
                        : "ELIMINATED",

                    Foreground = Brushes.Bisque,
                    FontSize = 13
                };

                TextBlock positionText = new TextBlock
                {
                    Text = $"TILE {playerPositions[player]}",
                    Foreground = Brushes.LightGray,
                    FontSize = 12
                };

                panel.Children.Add(name);
                panel.Children.Add(hpBar);
                panel.Children.Add(hpText);
                panel.Children.Add(positionText);

                border.Child = panel;

                PlayerStatusPanel.Children.Add(border);
            }
        }

        // =====================================
        // 현재 턴 표시
        // =====================================

        private void UpdateTurnDisplay()
        {
            if (gameEnded)
                return;

            int player = turnOrder[turnIndex];

            CurrentTurnText.Text = $"PLAYER {player}";

            CurrentTurnText.Foreground =
                GetPlayerColor(player);

            RoundText.Text =
                $"ROUND {currentRound:00} / {MaxRounds}";

            SuddenDeathText.Text =
                currentRound >= 20
                    ? "SUDDEN DEATH"
                    : "NORMAL MODE";

            SuddenDeathText.Foreground =
                currentRound >= 20
                    ? Brushes.OrangeRed
                    : Brushes.Bisque;
        }

        // =====================================
        // 주사위
        // =====================================

        private void RollDiceButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (isMoving || gameEnded)
                return;

            int player = turnOrder[turnIndex];

            if (playerHP[player] <= 0)
                return;

            int diceValue = random.Next(1, 7);

            DiceText.Text = diceValue.ToString();

            AddGameLog(
                $"PLAYER {player}: 주사위 {diceValue}");

            StartMove(player, diceValue);
        }

        // =====================================
        // 이동 시작
        // =====================================

        private void StartMove(int player, int diceValue)
        {
            isMoving = true;

            movingPlayer = player;

            remainingSteps = diceValue;

            RollDiceButton.IsEnabled = false;

            moveTimer?.Stop();

            // 캐릭터 걷기 시작
            if (characters.TryGetValue(
                player,
                out PixelCharacter? character))
            {
                character.StartWalking();
            }

            moveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(430)
            };

            moveTimer.Tick += MoveTimer_Tick;

            moveTimer.Start();
        }
        // =====================================
        // 타일 단위 이동
        // =====================================

        private void MoveTimer_Tick(
            object? sender,
            EventArgs e)
        {
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
                    Content = $"{i + 1}. {GetTileName(choice)} ({choice.Position.X}, {choice.Position.Y})",
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

        // =====================================
        // 이동 완료
        // =====================================

        private void FinishMove()
        {
            moveTimer?.Stop();

            isMoving = false;

            // 캐릭터 걷기 종료
            if (characters.TryGetValue(
                movingPlayer,
                out PixelCharacter? character))
            {
                character.StopWalking();
            }

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
        // =====================================
        // 타일 이벤트
        // =====================================

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
                AddGameLog(
                    $"PLAYER {player}: 시작 타일에 도착했습니다.");
            }
            else
            {
                switch (tile.Type)
                {
                    case TileType.Card:
                        playerCards[player]++;
                        AddGameLog(
                            $"PLAYER {player}: 카드 획득! (보유 {playerCards[player]}장)");
                        break;
                    case TileType.Heal:
                        playerHP[player] = Math.Min(MaxHP, playerHP[player] + tile.Amount);
                        AddGameLog($"PLAYER {player}: HP +{tile.Amount}");
                        break;
                    case TileType.Damage:
                    case TileType.Hole:
                        playerHP[player] = Math.Max(0, playerHP[player] - tile.Amount);
                        AddGameLog($"PLAYER {player}: HP -{tile.Amount}");
                        break;
                    case TileType.Prison:
                        AddGameLog($"PLAYER {player}: 감옥 타일에 도착했습니다.");
                        break;
                    case TileType.Teleport:
                        AddGameLog($"PLAYER {player}: 텔레포트 타일에 도착했습니다.");
                        break;
                }
            }

            // 20라운드부터 적용되는 임시 서든데스 규칙
            if (currentRound >= 20)
            {
                playerHP[player] = Math.Max(
                    0,
                    playerHP[player] - 5);

                AddGameLog(
                    $"PLAYER {player}: 서든데스 HP -5");
            }

            if (playerHP[player] <= 0)
            {
                if (characters.TryGetValue(
                    player,
                    out PixelCharacter? character))
                {
                    character.Visibility =
                        Visibility.Collapsed;
                }

                AddGameLog(
                    $"PLAYER {player} 탈락!");
            }
        }

        // =====================================
        // 다음 턴
        // =====================================

        private void NextTurn()
        {
            int previousIndex = turnIndex;

            // 다음 생존 플레이어 탐색
            for (int i = 0; i < turnOrder.Count; i++)
            {
                turnIndex++;

                if (turnIndex >= turnOrder.Count)
                {
                    turnIndex = 0;

                    currentRound++;
                }

                int player = turnOrder[turnIndex];

                if (playerHP[player] > 0)
                {
                    if (currentRound > MaxRounds)
                    {
                        EndGameByHP();
                        return;
                    }

                    DiceText.Text = "?";

                    RollDiceButton.IsEnabled = true;

                    UpdateTurnDisplay();

                    AddGameLog(
                        $"PLAYER {player} 턴 시작");

                    return;
                }
            }

            // 생존자가 없다면 게임 종료
            EndGameByHP();
        }

        // =====================================
        // 게임 종료 검사
        // =====================================

        private bool CheckGameEnd()
        {
            List<int> alivePlayers =
                playerHP
                    .Where(p => p.Value > 0)
                    .Select(p => p.Key)
                    .ToList();

            if (alivePlayers.Count <= 1)
            {
                int? winner =
                    alivePlayers.Count == 1
                        ? alivePlayers[0]
                        : null;

                EndGame(winner);

                return true;
            }

            return false;
        }

        private void EndGameByHP()
        {
            int maxHP = playerHP.Values.Max();

            List<int> leaders = playerHP
                .Where(p => p.Value == maxHP)
                .Select(p => p.Key)
                .ToList();

            // 최고 HP 동점이면 공동 결과 처리
            int? winner =
                leaders.Count == 1
                    ? leaders[0]
                    : null;

            EndGame(winner);
        }

        private void EndGame(int? winner)
        {
            if (gameEnded)
                return;

            gameEnded = true;

            moveTimer?.Stop();

            RollDiceButton.IsEnabled = false;

            if (winner.HasValue)
            {
                AddGameLog(
                    $"PLAYER {winner.Value} 승리!");
            }
            else
            {
                AddGameLog("게임 종료: 공동 결과");
            }

            GameFinished?.Invoke(this, winner);
        }

        // =====================================
        // 게임 로그
        // =====================================

        private void AddGameLog(string message)
        {
            TextBlock log = new TextBlock
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

        // =====================================
        // 종료 버튼
        // =====================================

        private void ExitButton_Click(
     object sender,
     RoutedEventArgs e)
        {
            moveTimer?.Stop();

            // 모든 캐릭터 애니메이션 정지
            foreach (PixelCharacter character in characters.Values)
            {
                character.StopWalking();
            }

            ExitRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
