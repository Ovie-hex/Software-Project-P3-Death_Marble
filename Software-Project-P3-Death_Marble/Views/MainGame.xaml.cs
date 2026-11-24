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
        private readonly Dictionary<int, List<GameCard>> playerCards = new();
        private readonly Dictionary<int, int> preferred = new(), prison = new(), bonus = new();
        private readonly HashSet<int> shield = new();
        private readonly Dictionary<int, (int Owner, int Expires, Border Marker)> traps = new();
        private readonly Dictionary<int, List<int>> paths = new();
        private readonly Random rng = new();
        private bool cardUsed;
        private static readonly GameCard[] Deck = {
            new("지옥의 불꽃", "상대 HP -20", 0), new("악마의 분노", "모든 상대 HP -10", 1),
            new("영혼 강탈", "상대 -15 / 자신 +15", 2), new("생명의 축복", "자신 HP +20", 3),
            new("악마의 방패", "다음 피해 방어", 4), new("순간이동", "상대와 위치 교환", 5),
            new("악마의 질주", "이번 이동 +3", 6), new("운명의 조작", "이번 주사위 최솟값 4", 7),
            new("지옥의 가시", "선택 타일에 함정 설치", 8), new("속박의 사슬", "상대 다음 턴 정지", 9),
            new("악마의 손길", "상대 카드 1장 획득", 10), new("혼돈의 계약", "무작위 HP +25 또는 -25", 11)
        };
        private record GameCard(string Name, string Description, int Kind);
        private readonly HashSet<int> controlledDice = new();

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

        public MainGame(int playerCount, List<int> turnOrder) : this(playerCount, turnOrder, null) { }

        public MainGame(int playerCount, List<int> turnOrder, Dictionary<int,int>? preferences)
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

            foreach (int id in turnOrder) preferred[id] = preferences != null && preferences.TryGetValue(id, out int n) ? Math.Clamp(n,1,6) : 1;
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
                playerCards[i] = new List<GameCard> { Deck[rng.Next(Deck.Length)] };
                prison[i] = 0; bonus[i] = 0;
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

            paths.Clear();
            // 9 x 9 외곽 순환 + 두 내부 지름길
            void Add(int x, int y) => tilePositions.Add(new Point(22 + x * 88, 22 + y * 88));
            for (int x = 0; x < 9; x++) Add(x, 0);
            for (int y = 1; y < 9; y++) Add(8, y);
            for (int x = 7; x >= 0; x--) Add(x, 8);
            for (int y = 7; y >= 1; y--) Add(0, y);
            for (int i = 0; i < 32; i++) paths[i] = new List<int> { (i + 1) % 32 };
            // 위쪽 2번에서 아래쪽 22번으로
            for (int y = 1; y <= 7; y++) Add(2, y);
            paths[2].Add(32);
            for (int i = 32; i < 39; i++) paths[i] = new List<int> { i == 38 ? 22 : i + 1 };
            // 오른쪽 12번에서 내부 경로 35번으로 합류
            for (int x = 7; x >= 3; x--) Add(x, 4);
            paths[12].Add(39);
            for (int i = 39; i < 44; i++) paths[i] = new List<int> { i == 43 ? 35 : i + 1 };
            for (int x = 3; x <= 7; x++) Add(x, 6);
            paths[37].Add(44);
            for (int i = 44; i < 49; i++) paths[i] = new List<int> { i == 48 ? 14 : i + 1 };
            // 타일 연결선
            for (int i = 0; i < tilePositions.Count; i++)
            foreach (int next in paths[i])
            {
                Point from = tilePositions[i];
                Point to = tilePositions[next];

                Line line = new Line
                {
                    X1 = from.X + TileSize / 2.0,
                    Y1 = from.Y + TileSize / 2.0,
                    X2 = to.X + TileSize / 2.0,
                    Y2 = to.Y + TileSize / 2.0,
                    Stroke = i >= 32 || next >= 32 ? Brushes.MediumPurple : Brushes.DarkGoldenrod,
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
            else if (index == 11 || index == 31) { background = Brushes.DarkSlateGray; tileText = "PRISON"; }
            else if (index == 16 || index == 34) { background = Brushes.Teal; tileText = "WARP"; }
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
                        $"CARD {playerCards[player].Count} | PICK {preferred[player]}",
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

            RefreshHand();
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

            if (prison[player] > 0) { prison[player]--; AddGameLog($"PLAYER {player}: 이동 제한"); FinishSkippedTurn(player); return; }
            isRolling = true;
            RefreshHand();

            RollDiceButton.IsEnabled = false;

            DiceText.Text = "ROLLING...";

            AddGameLog(
                $"PLAYER {player}: 주사위 굴리는 중...");

            // 3D 주사위 회전
            int result = DiceProbability.Roll(rng, preferred[player], playerHP[player], MaxHP);
            if (controlledDice.Remove(player)) result = Math.Max(4, result);
            GameDice.Roll(result);
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
            StartMove(player, result + bonus[player]);
            bonus[player] = 0;
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

            var choices = paths[currentPosition];
            int nextPosition = choices[0];
            if (choices.Count > 1) {
                moveTimer?.Stop();
                int recommended = PathAdvisor.Recommend(choices, remainingSteps, paths, t => TileScore(t, movingPlayer));
                int? choice = Choose("운명의 갈림길", choices.Select(t => (t, $"{(t >= 32 ? "내부 경로" : "외곽 경로")} · 타일 {t}\n예상 평가: {PathAdvisor.Score(t, remainingSteps, paths, n => TileScore(n, movingPlayer)):+0;-0;0}")), recommended,
                    $"크크크… 남은 이동은 {remainingSteps}칸. 현재 HP {playerHP[movingPlayer]}와 도착 타일의 효과를 고려하면 {recommended}번 방향이 유리하겠군.");
                nextPosition = choice ?? recommended;
                moveTimer?.Start();
            }

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
            if (traps.TryGetValue(position, out var trap) && trap.Owner != player) {
                BoardCanvas.Children.Remove(trap.Marker); traps.Remove(position); ChangeHP(player,-20); AddGameLog("함정 발동!");
            }
            if (playerHP[player] <= 0) return;
            if (position == 11 || position == 31) { prison[player]++; AddGameLog("다음 턴 이동 제한"); }
            else if (position == 16 || position == 34) { Relocate(player, rng.Next(tilePositions.Count)); AddGameLog("순간이동"); }
            else if (position != 0 && position % 9 == 0) {
                if (playerCards[player].Count < 3) { var card = Deck[rng.Next(Deck.Length)]; playerCards[player].Add(card); AddGameLog($"카드 획득: {card.Name}"); }
                else AddGameLog("카드 보유 한도 3장");
            }
            else if (position != 0 && position % 7 == 0) ChangeHP(player,15);
            else if (position != 0 && position % 5 == 0) ChangeHP(player,-15);
            ApplySuddenDeathTo(player);
        }

        private void ApplySuddenDeathTo(int player) {
            if (currentRound >= 20 && playerHP[player] > 0) ChangeHP(player, -(5 + (currentRound-20)/5*5));
        }
        private void FinishSkippedTurn(int player) {
            bonus[player]=0; controlledDice.Remove(player); ApplySuddenDeathTo(player); UpdatePlayerStatus(); if (!CheckGameEnd()) NextTurn();
        }
        private void ChangeHP(int player, int delta) {
            if (playerHP[player] <= 0) return;
            if (delta < 0 && shield.Remove(player)) { AddGameLog($"PLAYER {player}: 방패 발동"); return; }
            int old = playerHP[player]; playerHP[player] = Math.Clamp(old+delta,0,MaxHP);
            int change = playerHP[player]-old;
            if (change != 0) { if(change<0) characters[player].PlayDamageEffect(); else characters[player].PlayHealEffect(); ShowHPChangeText(player,change); AddGameLog($"PLAYER {player}: HP {change:+0;-0}"); }
            if(playerHP[player]==0) { characters[player].Visibility=Visibility.Collapsed; AddGameLog($"PLAYER {player} 탈락"); }
        }
        private void Relocate(int player,int tile) { playerPositions[player]=tile; UpdateCharacterPosition(player); }
        private int TileScore(int t,int player) {
            int score = t==0 ? 0 : t==11 || t==31 ? -20 : t==16 || t==34 ? -3 : t%9==0 ? 12 : t%7==0 ? Math.Min(15,MaxHP-playerHP[player]) : t%5==0 ? -15 : 0;
            if(traps.TryGetValue(t,out var trap) && trap.Owner!=player) score-=20; return score;
        }
        private int? Choose(string title, IEnumerable<(int Id,string Label)> options, int? recommended = null, string? dialogue = null)
        {
            return FateDialog.Choose(Window.GetWindow(this), title, options, recommended, dialogue);
        }
        private void RefreshHand() {
            HandPanel.Children.Clear(); int player=turnOrder[turnIndex];
            foreach(var card in playerCards[player].ToArray()) {
                var button=new Button { Content=new TextBlock { Text=card.Name+"\n"+card.Description, TextWrapping=TextWrapping.Wrap, Foreground=Brushes.Bisque }, Width=145, Height=72, Margin=new Thickness(4), Background=Brushes.DarkSlateBlue, BorderBrush=Brushes.Goldenrod, IsEnabled=!cardUsed&&!isMoving&&!isRolling&&!gameEnded };
                button.Click += (_,_)=>UseGameCard(card); HandPanel.Children.Add(button);
            }
        }
        private void UseGameCard(GameCard card) {
            if(cardUsed||isMoving||isRolling||gameEnded||isClosing) return;
            int p=turnOrder[turnIndex]; if(!playerCards[p].Contains(card)) return;
            int target=0;
            if(card.Kind is 0 or 2 or 5 or 9 or 10) {
                int? selected=Choose("대상 선택", playerHP.Where(x=>x.Key!=p&&x.Value>0&&(card.Kind!=10||playerCards[x.Key].Count>0)).Select(x=>(x.Key,$"PLAYER {x.Key} · HP {x.Value} · 카드 {playerCards[x.Key].Count}장")));
                if(!selected.HasValue) return; target=selected.Value;
            }
            if(card.Kind==8) {
                int? tile=Choose("함정 설치 타일", Enumerable.Range(1,tilePositions.Count-1).Where(t=>!traps.ContainsKey(t)).Select(t=>(t,$"타일 {t}")));
                if(!tile.HasValue) return;
                var marker=new Border { Width=TileSize, Height=TileSize, BorderBrush=Brushes.MediumPurple, BorderThickness=new Thickness(5), IsHitTestVisible=false };
                Canvas.SetLeft(marker,tilePositions[tile.Value].X); Canvas.SetTop(marker,tilePositions[tile.Value].Y); BoardCanvas.Children.Add(marker); traps[tile.Value]=(p,currentRound+3,marker);
            }
            switch(card.Kind) {
                case 0: ChangeHP(target,-20); break;
                case 1: foreach(int other in turnOrder.Where(x=>x!=p)) ChangeHP(other,-10); break;
                case 2: ChangeHP(target,-15); ChangeHP(p,15); break;
                case 3: ChangeHP(p,20); break;
                case 4: shield.Add(p); break;
                case 5: int old=playerPositions[p]; Relocate(p,playerPositions[target]); Relocate(target,old); break;
                case 6: bonus[p]=3; break;
                case 7: controlledDice.Add(p); break;
                case 9: prison[target]++; break;
                case 10: var stolen=playerCards[target][rng.Next(playerCards[target].Count)]; playerCards[target].Remove(stolen); playerCards[p].Add(stolen); break;
                case 11: ChangeHP(p,rng.Next(2)==0?25:-25); break;
            }
            playerCards[p].Remove(card); cardUsed=true; AddGameLog($"PLAYER {p}: {card.Name} 사용"); UpdatePlayerStatus(); RefreshHand(); if(!CheckGameEnd()&&playerHP[p]==0) NextTurn();
        }
        public List<Player> ResultPlayers() => playerHP.Select(x=> { var p=new Player(x.Key,$"PLAYER {x.Key}",new TileData()); p.TakeDamage(MaxHP-x.Value); return p; }).ToList();

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
                    foreach(var t in traps.Where(x=>x.Value.Expires<=currentRound).Select(x=>x.Key).ToArray()) { BoardCanvas.Children.Remove(traps[t].Marker); traps.Remove(t); }
                }

                int player = turnOrder[turnIndex];

                if (playerHP[player] > 0)
                {
                    if (currentRound > MaxRounds)
                    {
                        EndGameByHP();
                        return;
                    }

                    cardUsed = false;
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