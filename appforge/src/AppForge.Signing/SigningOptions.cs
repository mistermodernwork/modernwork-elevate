namespace AppForge.Signing;

/// <summary>Konfigurationsabschnitt <c>Signing</c>.</summary>
public sealed class SigningOptions
{
    public const string Section = "Signing";

    /// <summary>Pfad zu <c>jsign.jar</c> (Job-Image: /opt/jsign/jsign.jar) oder zum jsign-Starter.</summary>
    public string JsignPath { get; set; } = "/opt/jsign/jsign.jar";

    public string JavaPath { get; set; } = "java";

    /// <summary>signtool.exe (nur Windows-Worker); leer = Suche im Windows SDK.</summary>
    public string? SignToolPath { get; set; }

    /// <summary>Azure.CodeSigning.Dlib.dll für signtool + Artifact Signing (nur Windows-Worker).</summary>
    public string? ArtifactSigningDlibPath { get; set; }

    public string PowerShellPath { get; set; } = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh";

    /// <summary>Signier-Engine: Auto (Linux → jsign, Windows → signtool), Jsign, SignTool, PowerShell.</summary>
    public SigningEngineKind Engine { get; set; } = SigningEngineKind.Auto;

    /// <summary>PFX-Zertifikate dürfen nur verwendet werden, wenn sie als Test markiert sind – es sei denn, dies ist true.</summary>
    public bool AllowPfxInProduction { get; set; }

    public string DefaultTimestampUrl { get; set; } = "http://timestamp.acs.microsoft.com";

    public int TimestampRetries { get; set; } = 3;

    public string? ProxyUrl { get; set; }

    /// <summary>Maximale Dateigröße pro Upload (Bytes).</summary>
    public long MaxFileSize { get; set; } = 512L * 1024 * 1024;

    public string[] AllowedExtensions { get; set; } =
        [".ps1", ".psm1", ".psd1", ".ps1xml", ".exe", ".dll", ".msi", ".msix", ".cat", ".cab", ".vbs", ".js", ".sys", ".appx"];
}

public enum SigningEngineKind
{
    Auto,
    Jsign,
    SignTool,
    PowerShell,
}
