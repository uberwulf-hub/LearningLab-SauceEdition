using Avalonia.Controls;

namespace LearningLab.Views;

public partial class MainWindow : Window
{
    public MainWindow() { InitializeComponent(); Closing += (s, e) => MainView.RaiseAppClosing(); }
}
