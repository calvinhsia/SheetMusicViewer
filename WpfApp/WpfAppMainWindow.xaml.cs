using System.ComponentModel;
using System.Runtime.CompilerServices;
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

namespace WpfApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class WpfAppMainWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public void OnMyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        private int _TouchCount = 5;
        public int TouchCount { get { return _TouchCount; } set { _TouchCount = value; OnMyPropertyChanged(); } }
        public WpfAppMainWindow()
        {
            InitializeComponent();
            this.WindowState = WindowState.Maximized;
            this.DataContext = this;
        }

        private void Grid_MouseDown(object sender, MouseButtonEventArgs e)
        {
            TouchCount++;

        }
    }
}