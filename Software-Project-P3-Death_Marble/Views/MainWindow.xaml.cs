using System;
using System.Collections.Generic;
using System.Windows;

namespace Software_Project_P3_Death_Marble.Views
{
    public partial class MainWindow : Window
    {
        private GameSettings? currentSettings;

        private List<int> currentTurnOrder = new();

        public MainWindow()
        {
            InitializeComponent();

            new Data_Manager();
            ShowInitPage();
        }

        // =====================================
        // 시작 화면
        // =====================================

        private void ShowInitPage()
        {
            MainContainer.Children.Clear();

            InitPage page = new InitPage();

            page.StartRequested += (s, e) =>
            {
                ShowSettingPage();
            };

            MainContainer.Children.Add(page);
        }

        // =====================================
        // 설정 화면
        // =====================================

        private void ShowSettingPage()
        {
            MainContainer.Children.Clear();

            SettingPage page = new SettingPage();

            page.BackRequested += (s, e) =>
            {
                ShowInitPage();
            };

            page.StageCreateRequested += (s, e) =>
            {
                ShowStageCustomPage(null);
            };

            page.StageEditRequested += stage =>
            {
                ShowStageCustomPage(stage);
            };

            page.GameStartRequested += (s, settings) =>
            {
                currentSettings = settings;

                currentTurnOrder.Clear();

                ShowRoulettePage(settings.PlayerCount);
            };

            MainContainer.Children.Add(page);
        }

        // =====================================
        // 룰렛 화면
        // =====================================

        private void ShowRoulettePage(int playerCount)
        {
            MainContainer.Children.Clear();

            RoulettePage page =
                new RoulettePage(playerCount);

            page.RouletteCompleted += (s, turnOrder) =>
            {
                currentTurnOrder =
                    new List<int>(turnOrder);

                ShowMainGame();
            };

            MainContainer.Children.Add(page);
        }

        // =====================================
        // 메인 게임 화면
        // =====================================

        private void ShowMainGame()
        {
            if (currentSettings == null)
                return;

            MainContainer.Children.Clear();

            MainGame page = new MainGame(
                currentSettings.PlayerCount,
                currentTurnOrder,
                currentSettings.Stage);

            page.ExitRequested += (s, e) =>
            {
                ShowSettingPage();
            };

            page.GameFinished += (s, winner) =>
            {
                string message = winner.HasValue
                    ? $"PLAYER {winner.Value} 승리!"
                    : "게임이 공동 결과로 종료되었습니다.";

                MessageBox.Show(
                    message,
                    "DEATH MARBLE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                ShowSettingPage();
            };

            MainContainer.Children.Add(page);
        }

        // =====================================
        // 스테이지 제작 화면
        // =====================================

        private void ShowStageCustomPage(StageData? stageToEdit)
        {
            MainContainer.Children.Clear();

            StageCustomPage page =
                new StageCustomPage(stageToEdit);

            page.BackRequested += (s, e) =>
            {
                ShowSettingPage();
            };

            MainContainer.Children.Add(page);
        }
    }
}
