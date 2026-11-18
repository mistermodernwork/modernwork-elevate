namespace ModernWorkElevate.AppAvalonia.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

/// <summary>
/// Backing model for the "new version available" window, filled by
/// <see cref="Tray.UpdateController"/>. UI text is English per the project conventions.
/// </summary>
public sealed partial class UpdatePromptViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VersionText))]
    private string _newVersion = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VersionText))]
    private string _currentVersion = string.Empty;

    /// <summary>
    /// Version line, e.g. <c>"Version 1.3.6 → 1.4.0"</c>. Users see this popup out of
    /// context, so it names both versions.
    /// </summary>
    public string VersionText => string.IsNullOrEmpty(CurrentVersion)
        ? $"Version {NewVersion}"
        : $"Version {CurrentVersion} → {NewVersion}";
}
