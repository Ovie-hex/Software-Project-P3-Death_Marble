using System;
using System.Windows;
using System.Windows.Controls;

namespace Software_Project_P3_Death_Marble.Views
{
    public partial class InitPage : UserControl
    {
        // 게임 시작 이벤트
        public event EventHandler? StartRequested;

        public InitPage()
        {
            InitializeComponent();
        }

        // 게임 시작 버튼
        private void StartButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            StartRequested?.Invoke(
                this,
                EventArgs.Empty);
        }

        // 게임 종료 버튼
        private void ExitButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}