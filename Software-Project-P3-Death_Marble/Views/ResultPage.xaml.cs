using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Software_Project_P3_Death_Marble.Views
{
    public partial class ResultPage : UserControl
    {
        // 다시 플레이 이벤트
        public event EventHandler? ReplayRequested;

        // 메인 메뉴 이벤트
        public event EventHandler? MainMenuRequested;

        public ResultPage()
        {
            InitializeComponent();
        }

        // ========================================
        // 결과 표시
        // ========================================

        public void ShowResult(
            List<Player> players,
            Player? winner)
        {
            RankingPanel.Children.Clear();

            if (winner != null)
            {
                WinnerTitleText.Text = "★ THE SURVIVOR ★";
                WinnerNameText.Text = winner.Name;
                WinnerMessageText.Text = "FINAL WINNER";
            }
            else
            {
                WinnerTitleText.Text = "NO SURVIVOR";
                WinnerNameText.Text = "DRAW";
                WinnerMessageText.Text = "GAME OVER";
            }

            // HP 기준 정렬
            List<Player> ranking = players
                .OrderByDescending(p => p.HP)
                .ToList();

            for (int i = 0; i < ranking.Count; i++)
            {
                Player player = ranking[i];

                Border row = CreateRankingRow(
                    i + 1,
                    player,
                    player == winner);

                RankingPanel.Children.Add(row);
            }
        }

        // ========================================
        // 순위 행 생성
        // ========================================

        private Border CreateRankingRow(
            int rank,
            Player player,
            bool isWinner)
        {
            Border border = new Border
            {
                Background = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString(
                        isWinner ? "#65311F" : "#241519")),

                BorderBrush = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString(
                        isWinner ? "#F2C27D" : "#793B2A")),

                BorderThickness = new Thickness(2),
                Padding = new Thickness(15),
                Margin = new Thickness(0, 4, 0, 4)
            };

            Grid grid = new Grid();

            grid.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(70)
                });

            grid.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star)
                });

            grid.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(130)
                });

            // 순위
            TextBlock rankText = new TextBlock
            {
                Text = GetRankText(rank),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Gold,
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(rankText, 0);
            grid.Children.Add(rankText);

            // 플레이어 이름
            StackPanel playerInfo = new StackPanel();

            TextBlock nameText = new TextBlock
            {
                Text = player.Name,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Bisque
            };

            TextBlock statusText = new TextBlock
            {
                Text = player.IsEliminated
                    ? "ELIMINATED"
                    : "SURVIVED",

                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,

                Foreground = player.IsEliminated
                    ? Brushes.IndianRed
                    : Brushes.LightGreen,

                Margin = new Thickness(0, 5, 0, 0)
            };

            playerInfo.Children.Add(nameText);
            playerInfo.Children.Add(statusText);

            Grid.SetColumn(playerInfo, 1);
            grid.Children.Add(playerInfo);

            // HP
            TextBlock hpText = new TextBlock
            {
                Text = $"HP {player.HP}/{player.MaxHP}",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Bisque,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(hpText, 2);
            grid.Children.Add(hpText);

            border.Child = grid;

            return border;
        }

        // ========================================
        // 순위 문자열
        // ========================================

        private string GetRankText(int rank)
        {
            return rank switch
            {
                1 => "1ST",
                2 => "2ND",
                3 => "3RD",
                _ => $"{rank}TH"
            };
        }

        // ========================================
        // 버튼 이벤트
        // ========================================

        private void ReplayButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ReplayRequested?.Invoke(
                this,
                EventArgs.Empty);
        }

        private void MainMenuButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            MainMenuRequested?.Invoke(
                this,
                EventArgs.Empty);
        }
    }
}