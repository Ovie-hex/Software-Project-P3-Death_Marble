using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Software_Project_P3_Death_Marble.Views
{
    public partial class MainGame : UserControl
    {
        // =====================================
        // 기본 데이터
        // =====================================

        private readonly int playerCount;
        private readonly List<int> turnOrder;

        private readonly Dictionary<int, PixelCharacter> characters = new();
        private readonly Dictionary<int, int> playerPositions = new();
        private readonly Dictionary<int, int> playerHP = new();
        private readonly Dictionary<int, int> playerCards = new();

        private readonly List<Point> tilePositions = new();

        private DispatcherTimer? moveTimer;
        private DispatcherTimer? finishTimer;

        private int turnIndex;
        private int currentRound = 1;

        private int movingPlayer;
        private int remainingSteps;

        private bool isMoving;
        private bool isRolling;
        private bool gameEnded;
        private bool isClosing;

        private const int MaxHP = 100;
        private const int MaxRounds = 40;
        private const int TileSize = 76;

        public event EventHandler? ExitRequested;
        public event EventHandler<int?>? GameFinished;

        // =====================================
        // 생성자
        // =====================================

        public MainGame()
            : this(2, new List<int> { 1, 2 })
        {
        }

        public MainGame(int playerCount, List<int> turnOrder)
        {
            InitializeComponent();

            if (playerCount < 2 || playerCount > 4)
                throw new ArgumentOutOfRangeException(nameof(playerCount));

            if (turnOrder == null ||
                turnOrder.Count != playerCount ||
                turnOrder.Distinct().Count() != playerCount ||
                turnOrder.Any(p => p < 1 || p > playerCount))
            {
                throw new ArgumentException(
                    "플레이어 수 또는 턴 순서가 올바르지 않습니다.");
            }

            this.playerCount = playerCount;
            this.turnOrder = new List<int>(turnOrder);

            GameDice.RollCompleted += GameDice_RollCompleted;

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
            for (int i = 1; i <= playerCount; i++)
            {
                playerPositions[i] = 0;
                playerHP[i] = MaxHP;
                playerCards[i] = 0;
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

        // =====================================
        // 게임판 생성
        // =====================================

        private void CreateBoard()
        {
            BoardCanvas.Children.Clear();
            tilePositions.Clear();

            const int rows = 5;
            const int cols = 8;

            const double startX = 38;
            const double startY = 28;

            const double spacingX = 92;
            const double spacingY = 105;

            // 테스트용 뱀 형태 경로
            for (int row = 0; row < rows; row++)
            {
                for (int step = 0; step < cols; step++)
                {
                    int col = row % 2 == 0
                        ? step
                        : cols - 1 - step;

                    double x = startX + col * spacingX;
                    double y = startY + row * spacingY;

                    tilePositions.Add(new Point(x, y));
                }
            }

            // 타일 연결선
            for (int i = 0; i < tilePositions.Count - 1; i++)
            {
                Point from = tilePositions[i];
                Point to = tilePositions[i + 1];

                Line line = new Line
                {
                    X1 = from.X + TileSize / 2.0,
                    Y1 = from.Y + TileSize / 2.0,
                    X2 = to.X + TileSize / 2.0,
                    Y2 = to.Y + TileSize / 2.0,
                    Stroke = Brushes.DarkGoldenrod,
                    StrokeThickness = 7
                };

                BoardCanvas.Children.Add(line);
            }

            // 타일 표시
            for (int i = 0; i < tilePositions.Count; i++)
            {
                Border tile = CreateTile(i);

                Canvas.SetLeft(tile, tilePositions[i].X);
                Canvas.SetTop(tile, tilePositions[i].Y);

                BoardCanvas.Children.Add(tile);
            }
        }

        private Border CreateTile(int index)
        {
            Brush background;
            string tileText;

            if (index == 0)
            {
                background = Brushes.DarkRed;
                tileText = "START";
            }
            else if (index % 9 == 0)
            {
                background = Brushes.DarkSlateBlue;
                tileText = "CARD";
            }
            else if (index % 7 == 0)
            {
                background = Brushes.DarkGreen;
                tileText = "+HP";
            }
            else if (index % 5 == 0)
            {
                background = Brushes.Maroon;
                tileText = "-HP";
            }
            else
            {
                background = new SolidColorBrush(
                    Color.FromRgb(53, 35, 41));

                tileText = index.ToString();
            }

            Border border = new Border
            {
                Width = TileSize,
                Height = TileSize,
                Background = background,
                BorderBrush = Brushes.Goldenrod,
                BorderThickness = new Thickness(3)
            };

            TextBlock text = new TextBlock
            {
                Text = tileText,
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 4, 0, 0)
            };

            Grid grid = new Grid();
            grid.Children.Add(text);

            border.Child = grid;

            return border;
        }

        // =====================================
        // 캐릭터 생성
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

        private void UpdateAllCharacterPositions()
        {
            foreach (int player in characters.Keys)
                UpdateCharacterPosition(player);
        }

        private Point GetCharacterCanvasPosition(
            int player,
            int tileIndex)
        {
            Point tile = tilePositions[tileIndex];

            int offsetIndex = player - 1;

            double offsetX =
                offsetIndex % 2 == 0 ? 0 : 30;

            double offsetY =
                offsetIndex < 2 ? 18 : 36;

            return new Point(
                tile.X + offsetX,
                tile.Y + offsetY);
        }

        private void UpdateCharacterPosition(int player)
        {
            if (!characters.TryGetValue(
                player,
                out PixelCharacter? character))
                return;

            int position = playerPositions[player];

            if (position < 0 || position >= tilePositions.Count)
                return;

            Point point =
                GetCharacterCanvasPosition(player, position);

            Canvas.SetLeft(character, point.X);
            Canvas.SetTop(character, point.Y);
        }

        // =====================================
        // HP 변화 텍스트
        // =====================================

        private void ShowHPChangeText(int player, int amount)
        {
            if (amount == 0 || isClosing)
                return;

            if (!characters.ContainsKey(player))
                return;

            int position = playerPositions[player];

            Point point =
                GetCharacterCanvasPosition(player, position);

            TextBlock effectText = new TextBlock
            {
                Text = amount > 0
                    ? $"+{amount} HP"
                    : $"{amount} HP",

                Foreground = amount > 0
                    ? Brushes.LimeGreen
                    : Brushes.OrangeRed,

                FontFamily = new FontFamily("Consolas"),
                FontSize = 20,
                FontWeight = FontWeights.Black,

                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 5,
                    ShadowDepth = 2
                }
            };

            Canvas.SetLeft(effectText, point.X - 5);
            Canvas.SetTop(effectText, point.Y - 15);

            Panel.SetZIndex(effectText, 200);

            BoardCanvas.Children.Add(effectText);

            DoubleAnimation floatUp = new DoubleAnimation
            {
                From = point.Y - 15,
                To = point.Y - 50,
                Duration = TimeSpan.FromMilliseconds(900)
            };

            DoubleAnimation fadeOut = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(900)
            };

            fadeOut.Completed += (sender, args) =>
            {
                BoardCanvas.Children.Remove(effectText);
            };

            effectText.BeginAnimation(
                Canvas.TopProperty, floatUp);

            effectText.BeginAnimation(
                OpacityProperty, fadeOut);
        }

        // =====================================
        // 플레이어 상태 UI
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
                    Text = hp > 0
                        ? $"HP {hp} / {MaxHP}"
                        : "ELIMINATED",
                    Foreground = Brushes.Bisque,
                    FontSize = 13
                });

                panel.Children.Add(new TextBlock
                {
                    Text =
                        $"TILE {playerPositions[player]}  |  " +
                        $"CARD {playerCards[player]}",
                    Foreground = Brushes.LightGray,
                    FontSize = 12
                });

                border.Child = panel;

                PlayerStatusPanel.Children.Add(border);
            }
        }

        // =====================================
        // 턴 표시
        // =====================================

        private void UpdateTurnDisplay()
        {
            if (gameEnded)
                return;

            int player = turnOrder[turnIndex];

            CurrentTurnText.Text = $"PLAYER {player}";
            CurrentTurnText.Foreground = GetPlayerColor(player);

            RoundText.Text =
                $"ROUND {currentRound:00} / {MaxRounds}";

            bool suddenDeath = currentRound >= 20;

            SuddenDeathText.Text = suddenDeath
                ? "SUDDEN DEATH"
                : "NORMAL MODE";

            SuddenDeathText.Foreground = suddenDeath
                ? Brushes.OrangeRed
                : Brushes.Bisque;
        }

        // =====================================
        // 3D 주사위
        // =====================================

        private void RollDiceButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (isMoving || isRolling || gameEnded || isClosing)
                return;

            int player = turnOrder[turnIndex];

            if (playerHP[player] <= 0)
                return;

            isRolling = true;

            RollDiceButton.IsEnabled = false;

            DiceText.Text = "ROLLING...";

            AddGameLog(
                $"PLAYER {player}: 주사위 굴리는 중...");

            // 3D 주사위 회전
            GameDice.Roll();
        }

        private void GameDice_RollCompleted(
            object? sender,
            int result)
        {
            if (gameEnded || isClosing || !isRolling)
                return;

            isRolling = false;

            DiceText.Text = result.ToString();

            int player = turnOrder[turnIndex];

            AddGameLog(
                $"PLAYER {player}: 주사위 {result}");

            // 주사위 회전이 끝난 후 이동
            StartMove(player, result);
        }

        // =====================================
        // 캐릭터 이동
        // =====================================

        private void StartMove(int player, int diceValue)
        {
            isMoving = true;

            movingPlayer = player;
            remainingSteps = diceValue;

            RollDiceButton.IsEnabled = false;

            moveTimer?.Stop();
            finishTimer?.Stop();

            if (characters.TryGetValue(
                player,
                out PixelCharacter? character))
            {
                character.StartWalking();
            }

            moveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(350)
            };

            moveTimer.Tick += MoveTimer_Tick;

            moveTimer.Start();
        }

        private void MoveTimer_Tick(
            object? sender,
            EventArgs e)
        {
            if (gameEnded || isClosing || !isMoving)
                return;

            if (remainingSteps <= 0)
            {
                FinishMove();
                return;
            }

            int currentPosition =
                playerPositions[movingPlayer];

            int nextPosition =
                (currentPosition + 1) % tilePositions.Count;

            AnimateCharacterMove(
                movingPlayer, nextPosition);

            playerPositions[movingPlayer] = nextPosition;

            remainingSteps--;

            UpdatePlayerStatus();

            if (remainingSteps == 0)
            {
                moveTimer?.Stop();

                finishTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(300)
                };

                finishTimer.Tick += FinishTimer_Tick;

                finishTimer.Start();
            }
        }

        private void FinishTimer_Tick(
            object? sender,
            EventArgs e)
        {
            finishTimer?.Stop();

            if (!gameEnded && !isClosing && isMoving)
                FinishMove();
        }

        private void AnimateCharacterMove(
            int player,
            int nextPosition)
        {
            if (!characters.TryGetValue(
                player,
                out PixelCharacter? character))
                return;

            Point target =
                GetCharacterCanvasPosition(player, nextPosition);

            double currentX = Canvas.GetLeft(character);
            double currentY = Canvas.GetTop(character);

            character.BeginAnimation(
                Canvas.LeftProperty, null);

            character.BeginAnimation(
                Canvas.TopProperty, null);

            Canvas.SetLeft(character, currentX);
            Canvas.SetTop(character, currentY);

            DoubleAnimation moveX = new DoubleAnimation
            {
                From = currentX,
                To = target.X,
                Duration = TimeSpan.FromMilliseconds(280),
                FillBehavior = FillBehavior.Stop
            };

            DoubleAnimation moveY = new DoubleAnimation
            {
                From = currentY,
                To = target.Y,
                Duration = TimeSpan.FromMilliseconds(280),
                FillBehavior = FillBehavior.Stop
            };

            moveX.Completed += (sender, args) =>
            {
                character.BeginAnimation(
                    Canvas.LeftProperty, null);

                character.BeginAnimation(
                    Canvas.TopProperty, null);

                Canvas.SetLeft(character, target.X);
                Canvas.SetTop(character, target.Y);
            };

            character.BeginAnimation(
                Canvas.LeftProperty, moveX);

            character.BeginAnimation(
                Canvas.TopProperty, moveY);
        }

        private void FinishMove()
        {
            moveTimer?.Stop();
            finishTimer?.Stop();

            isMoving = false;

            if (characters.TryGetValue(
                movingPlayer,
                out PixelCharacter? character))
            {
                character.StopWalking();
            }

            int position =
                playerPositions[movingPlayer];

            AddGameLog(
                $"PLAYER {movingPlayer}: 타일 {position} 도착");

            ApplyTileEvent(movingPlayer, position);

            UpdatePlayerStatus();

            if (CheckGameEnd())
                return;

            NextTurn();
        }

        // =====================================
        // 타일 이벤트
        // =====================================

        private void ApplyTileEvent(
            int player,
            int position)
        {
            int oldHP = playerHP[player];

            if (position == 0)
            {
                AddGameLog(
                    $"PLAYER {player}: START 도착");
            }
            else if (position % 9 == 0)
            {
                playerCards[player]++;

                AddGameLog(
                    $"PLAYER {player}: 카드 획득! " +
                    $"(보유 {playerCards[player]}장)");
            }
            else if (position % 7 == 0)
            {
                playerHP[player] = Math.Min(
                    MaxHP,
                    playerHP[player] + 15);

                AddGameLog(
                    $"PLAYER {player}: HP 회복");
            }
            else if (position % 5 == 0)
            {
                playerHP[player] = Math.Max(
                    0,
                    playerHP[player] - 15);

                AddGameLog(
                    $"PLAYER {player}: HP 감소");
            }

            // 임시 서든데스 규칙
            if (currentRound >= 20)
            {
                playerHP[player] = Math.Max(
                    0,
                    playerHP[player] - 5);

                AddGameLog(
                    $"PLAYER {player}: 서든데스 HP -5");
            }

            // 실제 변화량
            int hpChange = playerHP[player] - oldHP;

            if (hpChange < 0)
            {
                if (characters.TryGetValue(
                    player,
                    out PixelCharacter? character))
                {
                    character.PlayDamageEffect();
                }

                ShowHPChangeText(player, hpChange);
            }
            else if (hpChange > 0)
            {
                if (characters.TryGetValue(
                    player,
                    out PixelCharacter? character))
                {
                    character.PlayHealEffect();
                }

                ShowHPChangeText(player, hpChange);
            }

            if (playerHP[player] <= 0)
            {
                AddGameLog(
                    $"PLAYER {player} 탈락!");

                if (characters.TryGetValue(
                    player,
                    out PixelCharacter? character))
                {
                    character.Visibility =
                        Visibility.Collapsed;
                }
            }
        }

        // =====================================
        // 다음 턴
        // =====================================

        private void NextTurn()
        {
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

                    DiceText.Text = "READY";

                    RollDiceButton.IsEnabled = true;

                    UpdateTurnDisplay();

                    AddGameLog(
                        $"PLAYER {player} 턴 시작");

                    return;
                }
            }

            EndGameByHP();
        }

        // =====================================
        // 게임 종료
        // =====================================

        private bool CheckGameEnd()
        {
            List<int> alivePlayers = playerHP
                .Where(p => p.Value > 0)
                .Select(p => p.Key)
                .ToList();

            if (alivePlayers.Count <= 1)
            {
                int? winner = alivePlayers.Count == 1
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

            int? winner = leaders.Count == 1
                ? leaders[0]
                : null;

            EndGame(winner);
        }

        private void EndGame(int? winner)
        {
            if (gameEnded)
                return;

            gameEnded = true;

            StopAnimations();

            RollDiceButton.IsEnabled = false;

            if (winner.HasValue)
            {
                AddGameLog(
                    $"PLAYER {winner.Value} 승리!");
            }
            else
            {
                AddGameLog(
                    "게임 종료: 무승부");
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
                Text =
                    $"[{DateTime.Now:HH:mm:ss}] {message}",

                Foreground = Brushes.Bisque,

                FontFamily =
                    new FontFamily("Consolas"),

                FontSize = 13,

                Margin =
                    new Thickness(0, 2, 0, 2)
            };

            GameLogPanel.Children.Add(log);

            LogScrollViewer.ScrollToEnd();
        }

        // =====================================
        // 애니메이션 종료
        // =====================================

        private void StopAnimations()
        {
            moveTimer?.Stop();
            finishTimer?.Stop();

            GameDice.StopRolling();

            isMoving = false;
            isRolling = false;

            foreach (PixelCharacter character in characters.Values)
            {
                character.StopAllAnimations();
            }
        }

        // =====================================
        // 게임 종료 버튼
        // =====================================

        private void ExitButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (isClosing)
                return;

            isClosing = true;

            StopAnimations();

            ExitRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}