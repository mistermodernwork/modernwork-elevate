namespace ModernWorkElevate.AppAvalonia.Views;

using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ModernWorkElevate.AppAvalonia.Services;

/// <summary>
/// Standalone, always-on-top window that says a newer version is on GitHub. Created once
/// and shown / hidden by <see cref="Tray.UpdateController"/>; the buttons surface as plain
/// events so the controller decides what they do.
/// </summary>
public partial class UpdatePromptWindow : Window
{
    public UpdatePromptWindow()
    {
        InitializeComponent();
        WindowChrome.Attach(this);
    }

    /// <summary>Raised when the user clicks "Open release page".</summary>
    public event EventHandler? OpenReleasePageRequested;

    /// <summary>Raised when the user clicks "Later".</summary>
    public event EventHandler? LaterRequested;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnOpenReleasePageClick(object? sender, RoutedEventArgs e)
        => OpenReleasePageRequested?.Invoke(this, EventArgs.Empty);

    private void OnLaterClick(object? sender, RoutedEventArgs e)
        => LaterRequested?.Invoke(this, EventArgs.Empty);
}
