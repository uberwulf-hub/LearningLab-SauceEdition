using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LearningLab.Views;

namespace LearningLab;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        else if (ApplicationLifetime is IActivityApplicationLifetime activity)
            activity.MainViewFactory = () => new MainView();
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single)
            single.MainView = new MainView();
        base.OnFrameworkInitializationCompleted();
    }
}
