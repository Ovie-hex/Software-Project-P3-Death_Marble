using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

        private readonly Dictionary<int, PixelCharacter> characters = new();

        private readonly Dictionary<int, int> playerPositions = new();

        private readonly Dictionary<int, int> playerHP = new();

        private readonly Dictionary<int, int> playerCards = new();

        private readonly List<Point> tilePositions = new();

        private readonly List<Border> tileBorders = new();

        private DispatcherTimer? moveTimer;

        private int turnIndex = 0;

        private int currentRound = 1;

        private int remainingSteps = 0;

        private int movingPlayer = 0;

        private bool isMoving = false;

        private bool gameEnded = false;

        private const int MaxHP = 100;

        private const int MaxRounds = 40;

        private const int TileSize = 76;

        // =====================================
        // 이벤트
        // =====================================

        public event EventHandler? ExitRequested;

        public event EventHandler<int?>? GameFinished;

        // =====================================
        // 생성자
        // =====================================

        public MainGame()
            : this(2, new List<int> { 1, 2 })
        {
        }

        public MainGame(
            int playerCount,
            List<int> turnOrder)
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

            tileBorders.Clear();

            // 5행 × 8열 뱀 형태의 테스트 경로
            // 총 40개 타일
            int rows = 5;
            int cols = 8;

            double startX = 55;
            double startY = 45;

            double spacingX = 92;
            double spacingY = 105;

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

            // 타일 생성
            for (int i = 0; i < tilePositions.Count; i++)
            {
                Border tile = CreateTile(i);

                Point point = tilePositions[i];

                Canvas.SetLeft(tile, point.X);
                Canvas.SetTop(tile, point.Y);

                BoardCanvas.Children.Add(tile);

                tileBorders.Add(tile);
            }
        }

        // =====================================
        // 타일 생성
        // =====================================

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
                BorderThickness = new Thickness(3),
                CornerRadius = new CornerRadius(0)
            };

            TextBlock text = new TextBlock
            {
                Text = tileText,
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Consolas"),
                FontSize = index == 0 ? 13 : 16,
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

            Point tile = tilePositions[position];

            // 같은 타일에 4명이 있어도 겹치지 않도록 배치
            int offsetIndex = player - 1;

            double offsetX = offsetIndex % 2 == 0
                ? 4
                : 40;

            double offsetY = offsetIndex < 2
                ? 29
                : 49;

            Canvas.SetLeft(
                character,
                tile.X + offsetX);

            Canvas.SetTop(
                character,
                tile.Y + offsetY);
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
                Interval = TimeSpan.FromMilliseconds(350)
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

            int currentPosition =
                playerPositions[movingPlayer];

            // 마지막 타일 다음에는 시작 타일로
            int nextPosition =
                (currentPosition + 1) % tilePositions.Count;

            playerPositions[movingPlayer] = nextPosition;

            remainingSteps--;

            // 캐릭터를 다음 타일로 이동
            UpdateCharacterPosition(movingPlayer);

            UpdatePlayerStatus();

            if (remainingSteps == 0)
            {
                FinishMove();
            }
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
            if (position == 0)
            {
                AddGameLog(
                    $"PLAYER {player}: START 도착");

                return;
            }

            if (position % 9 == 0)
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
                    $"PLAYER {player}: HP +15");
            }
            else if (position % 5 == 0)
            {
                playerHP[player] = Math.Max(
                    0,
                    playerHP[player] - 15);

                AddGameLog(
                    $"PLAYER {player}: HP -15");
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