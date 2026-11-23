using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Software_Project_P3_Death_Marble.Views
{
    public partial class RoulettePage : UserControl
    {
        private readonly Random random = new Random();

        private readonly List<int> remainingPlayers = new();
        private readonly List<int> turnOrder = new();

        private bool isSpinning = false;

        public event EventHandler<List<int>>? RouletteCompleted;

        public RoulettePage(int playerCount)
        {
            InitializeComponent();

            if (playerCount < 2 || playerCount > 4)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(playerCount),
                    "플레이어 수는 2~4명이어야 합니다.");
            }

            for (int i = 1; i <= playerCount; i++)
            {
                remainingPlayers.Add(i);
            }

            DrawRoulette();
            UpdateTurnOrder();
        }

        // =====================================
        // 플레이어 색상
        // =====================================

        private Brush GetPlayerColor(int player)
        {
            return player switch
            {
                1 => Brushes.Firebrick,
                2 => Brushes.DarkOrange,
                3 => Brushes.MediumPurple,
                4 => Brushes.SeaGreen,
                _ => Brushes.DimGray
            };
        }

        // =====================================
        // 룰렛 원판 그리기
        // =====================================

        private void DrawRoulette()
        {
            RouletteCanvas.Children.Clear();

            int count = remainingPlayers.Count;

            if (count == 0)
                return;

            double centerX = 200;
            double centerY = 200;
            double radius = 185;

            double angleSize = 360.0 / count;

            for (int i = 0; i < count; i++)
            {
                double startAngle = i * angleSize - 90;
                double endAngle = startAngle + angleSize;

                PathGeometry geometry = new PathGeometry();

                PathFigure figure = new PathFigure
                {
                    StartPoint = new Point(centerX, centerY)
                };

                double startRadians = startAngle * Math.PI / 180;
                double endRadians = endAngle * Math.PI / 180;

                Point startPoint = new Point(
                    centerX + radius * Math.Cos(startRadians),
                    centerY + radius * Math.Sin(startRadians));

                Point endPoint = new Point(
                    centerX + radius * Math.Cos(endRadians),
                    centerY + radius * Math.Sin(endRadians));

                figure.Segments.Add(
                    new LineSegment(startPoint, true));

                // 두 명일 때는 180도 반원으로 처리
                figure.Segments.Add(
                    new ArcSegment
                    {
                        Point = endPoint,
                        Size = new Size(radius, radius),
                        SweepDirection = SweepDirection.Clockwise,
                        IsLargeArc = angleSize > 180
                    });

                figure.IsClosed = true;

                geometry.Figures.Add(figure);

                Path sector = new Path
                {
                    Data = geometry,
                    Fill = GetPlayerColor(remainingPlayers[i]),
                    Stroke = Brushes.Gold,
                    StrokeThickness = 4
                };

                RouletteCanvas.Children.Add(sector);

                // 플레이어 이름
                double middleAngle =
                    (startAngle + angleSize / 2) * Math.PI / 180;

                double textRadius = 115;

                TextBlock label = new TextBlock
                {
                    Text = $"P{remainingPlayers[i]}",
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 27,
                    FontWeight = FontWeights.Black,
                    Foreground = Brushes.White,
                    TextAlignment = TextAlignment.Center,
                    Width = 65
                };

                Canvas.SetLeft(
                    label,
                    centerX + textRadius * Math.Cos(middleAngle) - 32.5);

                Canvas.SetTop(
                    label,
                    centerY + textRadius * Math.Sin(middleAngle) - 18);

                RouletteCanvas.Children.Add(label);
            }
        }

        // =====================================
        // 룰렛 회전
        // =====================================

        private void SpinButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (isSpinning || remainingPlayers.Count == 0)
                return;

            isSpinning = true;
            SpinButton.IsEnabled = false;

            RouletteStatusText.Text = "룰렛 회전 중...";

            int selectedIndex = random.Next(remainingPlayers.Count);

            int selectedPlayer = remainingPlayers[selectedIndex];

            int count = remainingPlayers.Count;

            double angleSize = 360.0 / count;

            // 선택된 구역의 중앙이 위쪽 화살표를 향하도록 회전
            double targetAngle =
                -(selectedIndex + 0.5) * angleSize;

            double currentAngle = RouletteRotation.Angle;

            // 현재 회전 위치를 기준으로 목표 각도 계산
            double normalizedCurrent =
                ((currentAngle % 360) + 360) % 360;

            double normalizedTarget =
                ((targetAngle % 360) + 360) % 360;

            double delta =
                (normalizedTarget - normalizedCurrent + 360) % 360;

            double finalAngle =
                currentAngle + 360 * 5 + delta;

            DoubleAnimation animation = new DoubleAnimation
            {
                From = currentAngle,
                To = finalAngle,
                Duration = TimeSpan.FromSeconds(4),
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseOut
                },
                FillBehavior = FillBehavior.Stop
            };

            animation.Completed += (s, args) =>
            {
                RouletteRotation.BeginAnimation(
                    RotateTransform.AngleProperty,
                    null);

                RouletteRotation.Angle = 0;

                turnOrder.Add(selectedPlayer);
                remainingPlayers.Remove(selectedPlayer);

                RouletteStatusText.Text =
                    $"PLAYER {selectedPlayer} 선택!";

                UpdateTurnOrder();

                if (remainingPlayers.Count == 1)
                {
                    // 마지막 플레이어 자동 확정
                    turnOrder.Add(remainingPlayers[0]);
                    remainingPlayers.Clear();

                    UpdateTurnOrder();
                }

                if (remainingPlayers.Count == 0)
                {
                    SpinButton.Visibility = Visibility.Collapsed;

                    StartGameButton.Visibility = Visibility.Visible;

                    RouletteStatusText.Text =
                        "모든 턴 순서가 결정되었습니다!";
                }
                else
                {
                    DrawRoulette();

                    SpinButton.IsEnabled = true;
                }

                isSpinning = false;
            };

            RouletteRotation.BeginAnimation(
                RotateTransform.AngleProperty,
                animation);
        }

        // =====================================
        // 턴 순서 표시
        // =====================================

        private void UpdateTurnOrder()
        {
            TurnOrderPanel.Children.Clear();

            for (int i = 0; i < turnOrder.Count; i++)
            {
                int player = turnOrder[i];

                Border border = new Border
                {
                    Background = new SolidColorBrush(
                        Color.FromRgb(42, 23, 27)),
                    BorderBrush = GetPlayerColor(player),
                    BorderThickness = new Thickness(3),
                    Padding = new Thickness(12),
                    Margin = new Thickness(0, 5, 0, 5)
                };

                TextBlock text = new TextBlock
                {
                    Text = $"{i + 1}  →  PLAYER {player}",
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 17,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.Bisque
                };

                border.Child = text;

                TurnOrderPanel.Children.Add(border);
            }
        }

        // =====================================
        // 게임 시작
        // =====================================

        private void StartGameButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (remainingPlayers.Count > 0)
                return;

            RouletteCompleted?.Invoke(
                this,
                new List<int>(turnOrder));
        }
    }
}