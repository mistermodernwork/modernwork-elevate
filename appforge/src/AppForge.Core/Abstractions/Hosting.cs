namespace AppForge.Core.Abstractions;

public enum HostingMode
{
    /// <summary>Reine Cloud (Standard, Plan 3.2b).</summary>
    Azure,
    OnPrem,
    Hybrid,
}

/// <summary>Konfigurationsabschnitt <c>Hosting</c>.</summary>
public sealed class HostingOptions
{
    public const string Section = "Hosting";

    public HostingMode Mode { get; set; } = HostingMode.Azure;

    /// <summary>On-Prem: Worker läuft im selben Prozess wie die API (Ein-Server-Installation).</summary>
    public bool InProcessWorker { get; set; }

    public string DataDirectory { get; set; } = "data";

    public string? PublicBaseUrl { get; set; }
}
