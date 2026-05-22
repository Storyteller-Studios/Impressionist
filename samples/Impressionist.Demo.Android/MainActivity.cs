using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;

namespace Impressionist.Demo.Android
{
    [Activity(
        Label = "Impressionist.Demo.Android",
        Theme = "@style/MyTheme.NoActionBar",
        Icon = "@drawable/icon",
        MainLauncher = true,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
    public class MainActivity : AvaloniaMainActivity
    {
        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            return base.CustomizeAppBuilder(builder)
                .Configure<App>()
                .WithInterFont();
        }
    }
}
