using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using MUTEit.Core.Localization;

namespace MUTEit.App.SettingsUi;

/// <summary>Small confirmation dialog in the MUTEit style: Cancel on the left, the main action on the right.</summary>
public sealed class ConfirmWindow : Window
{
    public ConfirmWindow()
        : this(string.Empty, string.Empty, "OK")
    {
    }

    public ConfirmWindow(string title, string text, string okText)
    {
        Title = title;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var cancel = new Button { Content = IconText("IconClose", Loc.T("BtnCancel")), MinWidth = 120, HorizontalContentAlignment = HorizontalAlignment.Center };
        var ok = new Button { Content = IconText("IconReset", okText), MinWidth = 120, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Classes.Add("primary");
        cancel.Click += (_, _) => Close(false);
        ok.Click += (_, _) => Close(true);

        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brush("BrushTextSecondary") },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 10,
                    Margin = new Thickness(0, 8, 0, 0),
                    Children = { cancel, ok },
                },
            },
        };
    }

    private static StackPanel IconText(string icon, string text)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (Application.Current?.TryFindResource(icon, out var geometry) == true && geometry is Geometry g)
        {
            panel.Children.Add(new PathIcon { Data = g, Width = 16, Height = 16 });
        }

        panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }

    private static IBrush? Brush(string key) =>
        Application.Current?.TryFindResource(key, out var value) == true ? value as IBrush : null;
}
