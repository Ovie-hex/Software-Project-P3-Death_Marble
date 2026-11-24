using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Linq;

namespace Software_Project_P3_Death_Marble.Views
{
    public partial class MainWindow : Window
    {
        private GameSettings? currentSettings;

        private List<int> currentTurnOrder = new();
        private Dictionary<int,int> preferences = new();

        public MainWindow()
        {
            InitializeComponent();

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
                ShowStageCustomPage();
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

                ShowPreferences();
            };

            MainContainer.Children.Add(page);
        }

        // =====================================
        // 메인 게임 화면
        // =====================================

        private void ShowPreferences()
        {
            preferences.Clear();
            foreach (int id in currentTurnOrder)
            {
                int? pick = FateDialog.Choose(this, $"PLAYER {id} · 운명의 숫자", Enumerable.Range(1, 6).Select(n => (n, $"{"⚀⚁⚂⚃⚄⚅"[n-1]}   {n}")), null,
                    "체력이 높을수록 선택한 숫자가 나올 확률이 높아집니다.");
                if (!pick.HasValue) { ShowSettingPage(); return; }
                preferences[id] = pick.Value;
            }
            ShowMainGame();
        }

        private void ShowMainGame()
        {
            if (currentSettings == null)
                return;

            MainContainer.Children.Clear();

            MainGame page = new MainGame(
                currentSettings.PlayerCount,
                currentTurnOrder, preferences);

            page.ExitRequested += (s, e) =>
            {
                ShowSettingPage();
            };

            page.GameFinished += (s, winner) =>
            {
                var players=page.ResultPlayers();
                var result=new ResultPage(); result.ShowResult(players,players.FirstOrDefault(p=>p.Id==winner));
                result.ReplayRequested+=(_,_)=>ShowRoulettePage(currentSettings.PlayerCount);
                result.MainMenuRequested+=(_,_)=>ShowInitPage();
                MainContainer.Children.Clear(); MainContainer.Children.Add(result);
            };

            MainContainer.Children.Add(page);
        }

        // =====================================
        // 스테이지 제작 화면
        // =====================================

        private void ShowStageCustomPage()
        {
            MainContainer.Children.Clear();

            StageCustomPage page =
                new StageCustomPage();

            page.BackRequested += (s, e) =>
            {
                ShowSettingPage();
            };

            MainContainer.Children.Add(page);
        }
    }
}