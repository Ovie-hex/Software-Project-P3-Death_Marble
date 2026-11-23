using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Software_Project_P3_Death_Marble.Views
{
    public partial class SettingPage : UserControl
    {
        public event EventHandler? BackRequested;

        public event EventHandler? StageCreateRequested;

        public event EventHandler<GameSettings>? GameStartRequested;

        public SettingPage()
        {
            InitializeComponent();

            UpdatePlayerPreview();
        }

        // 플레이어 수 변경
        private void PlayerCountCombo_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (PlayerPreviewPanel == null)
                return;

            UpdatePlayerPreview();
        }

        // 참가 플레이어 표시
        private void UpdatePlayerPreview()
        {
            PlayerPreviewPanel.Children.Clear();

            int count = PlayerCountCombo.SelectedIndex + 2;

            for (int i = 1; i <= count; i++)
            {
                TextBlock playerText = new TextBlock
                {
                    Text = $"■ PLAYER {i}",
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 17,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(
                        Color.FromRgb(242, 194, 125)),
                    Margin = new Thickness(0, 5, 0, 5)
                };

                PlayerPreviewPanel.Children.Add(playerText);
            }
        }

        // 스테이지 제작
        private void CreateStageButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            StageCreateRequested?.Invoke(
                this,
                EventArgs.Empty);
        }

        // 뒤로 가기
        private void BackButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            BackRequested?.Invoke(
                this,
                EventArgs.Empty);
        }

        // 게임 시작
        private void StartGameButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (StageListBox.SelectedItem is not ListBoxItem selectedStage)
            {
                MessageBox.Show(
                    "스테이지를 선택하세요.",
                    "DEATH MARBLE");

                return;
            }

            GameSettings settings = new GameSettings
            {
                PlayerCount = PlayerCountCombo.SelectedIndex + 2,

                StageName = selectedStage.Content?.ToString()
                            ?? "기본 스테이지"
            };

            GameStartRequested?.Invoke(this, settings);
        }
    }

    // 게임 설정 데이터
    public class GameSettings : EventArgs
    {
        public int PlayerCount { get; set; }

        public string StageName { get; set; } = "";
    }
}