using Android.App;
using Android.Content.PM;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace LearningLab.Android;

[Activity(
    Label = "Learning Lab SE",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    /// <summary>The running activity (asking for the microphone needs it).</summary>
    public static MainActivity Current { get; private set; }

    protected override void OnCreate(global::Android.OS.Bundle savedInstanceState)
    {
        Current = this;
        AndroidPlatform.Install(this);
        base.OnCreate(savedInstanceState);
    }

    protected override void OnStop()
    {
        // Android may end the app while it is in the background: apps keep their work now (Cert Lab's exam or lab)
        LearningLab.Views.MainView.RaiseAppClosing();
        base.OnStop();
    }

    protected override void OnDestroy()
    {
        if (Current == this) Current = null;
        base.OnDestroy();
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        AndroidRecognizer.PermissionResult(requestCode, grantResults);
    }
}
