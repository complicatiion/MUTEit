using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace MUTEit.App.SettingsUi;

public partial class SettingsApp : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new SettingsViewModel();
            ThemeManager.Apply(viewModel.Settings);
            var window = new MainWindow { DataContext = viewModel };
            ThemeManager.ApplyBackdrop(window, viewModel.Settings);
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
