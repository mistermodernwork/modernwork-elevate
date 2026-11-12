namespace ModernWorkElevate.AppAvalonia.Services;

/// <summary>
/// Manages the per-user autostart entry. Uses the <c>HKCU</c> Run key only —
/// never <c>HKLM</c>, never a scheduled task.
/// </summary>
public interface IAutostartService
{
    /// <summary>Whether ModernWork Elevate is registered to start with Windows.</summary>
    bool IsEnabled { get; }

    /// <summary>Registers ModernWork Elevate to start with Windows for the current user.</summary>
    void Enable();

    /// <summary>Removes the autostart registration.</summary>
    void Disable();
}
