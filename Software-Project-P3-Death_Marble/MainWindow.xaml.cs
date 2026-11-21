using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Software_Project_P3_Death_Marble
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            // Init Modules
            new Data_Manager();
            new Stage_Customizer();
            new Game_Manager();
            new XAML_Manager();

            // Init XAML Controlls
            InitializeComponent();
        }
    }
}