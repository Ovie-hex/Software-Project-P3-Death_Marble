using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Software_Project_P3_Death_Marble
{
    /// <summary>
    /// Initial menu for Death Marble.
    /// </summary>
    public partial class InitPage : UserControl
    {
        public event EventHandler? StartRequested;

        public InitPage()
        {
            InitializeComponent();

            // Trim the transparent padding around the supplied button artwork.
            StartButtonImage.Source = LoadCroppedButtonImage("Start.png", new Int32Rect(13, 90, 807, 168));
            ExitButtonImage.Source = LoadCroppedButtonImage("Exit.png", new Int32Rect(13, 82, 807, 176));
        }

        private static CroppedBitmap LoadCroppedButtonImage(string fileName, Int32Rect crop)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri($"pack://application:,,,/Images/{fileName}", UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            var croppedBitmap = new CroppedBitmap(bitmap, crop);
            croppedBitmap.Freeze();
            return croppedBitmap;
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            StartRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current?.Shutdown();
        }
    }
}
