using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Software_Project_P3_Death_Marble.Views
{
    public partial class SettingPage : UserControl
    {
        private readonly Connector.BindableEvent loadStagesEvent;
        private readonly Connector.BindableEvent removeStageEvent;

        public event EventHandler? BackRequested;

        public event EventHandler? StageCreateRequested;

        public event EventHandler<GameSettings>? GameStartRequested;

        public event Action<StageData>? StageEditRequested;

        public SettingPage()
        {
            InitializeComponent();

            loadStagesEvent = Connector.GetBindableEvent(StageConnectorNames.LoadStages);
            loadStagesEvent.Name = StageConnectorNames.SettingPage;
            loadStagesEvent.OnInvoke = HandleStageLoadResult;
            removeStageEvent = Connector.GetBindableEvent(StageConnectorNames.RemoveStage);
            removeStageEvent.Name = StageConnectorNames.SettingPage;
            removeStageEvent.OnInvoke = HandleStageDeleteResult;
            Unloaded += (_, _) =>
            {
                loadStagesEvent.Destroy();
                removeStageEvent.Destroy();
            };

            UpdatePlayerPreview();
            RequestStageLoad();
        }

        private void RequestStageLoad()
        {
            loadStagesEvent.Fire(
                StageConnectorNames.DataManager,
                new LoadStagesRequest());
        }

        private void HandleStageLoadResult(string senderName, dynamic? payload)
        {
            if (payload is not StageEventResult result)
            {
                ShowStageLoadError("스테이지 로드 결과를 확인할 수 없습니다.");
                return;
            }

            if (!result.Success)
            {
                ShowStageLoadError(result.ErrorMessage ?? "알 수 없는 오류입니다.");
                return;
            }

            StageListBox.ItemsSource = result.Stages ?? new List<StageData>();
            StageListBox.SelectedIndex = StageListBox.Items.Count > 0 ? 0 : -1;
        }

        private static void ShowStageLoadError(string message)
        {
            MessageBox.Show(
                $"스테이지를 불러오지 못했습니다.\n{message}",
                "DEATH MARBLE",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        private void HandleStageDeleteResult(string senderName, dynamic? payload)
        {
            if (payload is not StageEventResult result)
            {
                MessageBox.Show(
                    "스테이지 삭제 결과를 확인할 수 없습니다.",
                    "DEATH MARBLE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            if (!result.Success)
            {
                MessageBox.Show(
                    $"스테이지를 삭제하지 못했습니다.\n{result.ErrorMessage}",
                    "DEATH MARBLE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            RequestStageLoad();
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

        private void DeleteStageButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (StageListBox.SelectedItem is not StageData selectedStage)
            {
                MessageBox.Show(
                    "삭제할 스테이지를 선택하세요.",
                    "DEATH MARBLE");
                return;
            }

            MessageBoxResult confirmation = MessageBox.Show(
                $"'{selectedStage.Name}' 스테이지를 삭제할까요?",
                "스테이지 삭제 확인",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirmation != MessageBoxResult.Yes)
                return;

            try
            {
                removeStageEvent.Fire(
                    StageConnectorNames.DataManager,
                    new RemoveStageRequest { StageId = selectedStage.Id });
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    $"스테이지 삭제 요청에 실패했습니다.\n{exception.Message}",
                    "DEATH MARBLE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void StageListBox_MouseDoubleClick(
            object sender,
            System.Windows.Input.MouseButtonEventArgs e)
        {
            if (StageListBox.SelectedItem is StageData selectedStage)
                StageEditRequested?.Invoke(selectedStage);
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
            if (StageListBox.SelectedItem is not StageData selectedStage)
            {
                MessageBox.Show(
                    "스테이지를 선택하세요.",
                    "DEATH MARBLE");

                return;
            }

            GameSettings settings = new GameSettings
            {
                PlayerCount = PlayerCountCombo.SelectedIndex + 2,
                Stage = selectedStage,
                StageName = selectedStage.Name
            };

            GameStartRequested?.Invoke(this, settings);
        }
    }

    // 게임 설정 데이터
    public class GameSettings : EventArgs
    {
        public int PlayerCount { get; set; }

        public string StageName { get; set; } = "";

        public StageData Stage { get; set; } = new();
    }
}
