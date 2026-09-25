using Microsoft.UI.Xaml;

namespace Codewright;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();

        try
        {
            await _window.InitializeAsync();
        }
        catch (Exception ex)
        {
            // Keep the unpackaged process alive long enough to surface a useful
            // startup error instead of silently terminating on an initialization
            // exception.
            await _window.ShowStartupErrorAsync(ex);
        }
    }
}
