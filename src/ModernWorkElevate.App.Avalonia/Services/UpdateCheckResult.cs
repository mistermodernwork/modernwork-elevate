namespace ModernWorkElevate.AppAvalonia.Services;

/// <summary>
/// A pending update discovered by <see cref="IUpdateService"/>. In this app
/// self-update is disabled (<see cref="UpdateService"/>), so no instance is ever created;
/// the type stays so the controller and view model compile unchanged.
/// </summary>
/// <param name="Version">The target version, e.g. <c>"0.3.0"</c>.</param>
public sealed record UpdateCheckResult(string Version);
