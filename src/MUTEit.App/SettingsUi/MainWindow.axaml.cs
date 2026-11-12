using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Transformation;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace MUTEit.App.SettingsUi;

public partial class MainWindow : Window, IDialogs
{
    private SettingsViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        Closing += (_, _) => _viewModel?.Shutdown();
        Opened += (_, _) => FitToScreen();

        // Recording a hotkey ends when the window loses the focus (the old hotkey stays).
        Deactivated += (_, _) => _viewModel?.CancelRecording();
        DataContextChanged += (_, _) =>
        {
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged -= OnViewModelChanged;
            }

            _viewModel = DataContext as SettingsViewModel;
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged += OnViewModelChanged;
                _viewModel.Dialogs = this;
            }
        };
    }

    /// <summary>Title bar and brand area move the window (client area extended into the title bar).</summary>
    private void OnDragAreaPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SettingsViewModel.Page) || _viewModel is null)
        {
            return;
        }

        PageScroll.Offset = default;
        Control page = _viewModel.Page switch
        {
            SettingsPage.Hotkey => PageHotkey,
            SettingsPage.Sounds => PageSounds,
            SettingsPage.Display => PageDisplay,
            SettingsPage.General => PageGeneral,
            SettingsPage.About => PageAbout,
            _ => PageMicrophone,
        };
        AnimateIn(page);
    }

    /// <summary>Soft page change: start invisible and 14 px lower, then let the style transitions run to the target.</summary>
    private static void AnimateIn(Control page)
    {
        page.Transitions = new Transitions();
        page.Opacity = 0;
        page.RenderTransform = TransformOperations.Parse("translateY(14px)");

        Dispatcher.UIThread.Post(() =>
        {
            page.ClearValue(Animatable.TransitionsProperty);
            page.Opacity = 1;
            page.RenderTransform = TransformOperations.Parse("translateY(0px)");
        }, DispatcherPriority.Render);
    }

    /// <summary>Default size, but never larger than 90 % of the work area (small / laptop screens).</summary>
    private void FitToScreen()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var scaling = screen.Scaling;
        var area = screen.WorkingArea;
        var maxWidth = area.Width / scaling * 0.9;
        var maxHeight = area.Height / scaling * 0.9;
        if (Width <= maxWidth && Height <= maxHeight)
        {
            return;
        }

        Width = Math.Max(MinWidth, Math.Min(Width, maxWidth));
        Height = Math.Max(MinHeight, Math.Min(Height, maxHeight));
        Position = new PixelPoint(
            area.X + (int)((area.Width - (Width * scaling)) / 2),
            area.Y + (int)((area.Height - (Height * scaling)) / 2));
    }

    // ------------------------------------------------------------------ IDialogs

    public async Task<string?> SaveFileAsync(string title, string suggestedName)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = "json",
            FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }],
        });
        return file?.TryGetLocalPath();
    }

    public async Task<string?> OpenFileAsync(string title, string filterName, IReadOnlyList<string> patterns)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(filterName) { Patterns = patterns }],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public Task<bool> ConfirmAsync(string title, string text, string okText) =>
        new ConfirmWindow(title, text, okText).ShowDialog<bool>(this);

    public void RefreshBackdrop()
    {
        if (_viewModel is not null)
        {
            ThemeManager.ApplyBackdrop(this, _viewModel.Settings);
        }
    }
}
