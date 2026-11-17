using AppForge.Core.Processes;

namespace AppForge.Signing.Engines;

public sealed record SignToolCall
{
    /// <summary>SHA1-Thumbprint im Speicher (CertStore).</summary>
    public string? Thumbprint { get; init; }

    public bool MachineStore { get; init; } = true;

    public string? PfxFile { get; init; }

    public string? PfxPassword { get; init; }

    /// <summary>Artifact Signing über die Dlib.</summary>
    public string? DlibPath { get; init; }

    public string? DlibMetadataFile { get; init; }

    public string? TimestampUrl { get; init; }

    public string? Description { get; init; }

    public string? DescriptionUrl { get; init; }

    public required IReadOnlyList<string> Files { get; init; }
}

/// <summary>signtool-Aufrufe (Windows-Worker), wie in build/sign.ps1 von ModernWork Elevate: SHA-256 + RFC-3161.</summary>
public static class SignToolCommand
{
    public static ProcessSpec BuildSign(string signToolPath, SignToolCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        var args = new List<string> { "sign", "/fd", "SHA256" };
        var secrets = new List<string>();
        if (call.DlibPath is not null)
        {
            args.AddRange(["/dlib", call.DlibPath, "/dmdf", call.DlibMetadataFile ?? throw new ArgumentException("Metadaten-Datei für die Dlib fehlt.", nameof(call))]);
        }
        else if (call.PfxFile is not null)
        {
            args.AddRange(["/f", call.PfxFile]);
            if (call.PfxPassword is not null)
            {
                args.AddRange(["/p", call.PfxPassword]);
                secrets.Add(call.PfxPassword);
            }
        }
        else if (call.Thumbprint is not null)
        {
            args.AddRange(["/sha1", call.Thumbprint]);
            if (call.MachineStore)
            {
                args.Add("/sm");
            }
        }
        else
        {
            throw new ArgumentException("Weder Thumbprint, PFX noch Dlib angegeben.", nameof(call));
        }

        if (!string.IsNullOrWhiteSpace(call.TimestampUrl))
        {
            args.AddRange(["/tr", call.TimestampUrl, "/td", "SHA256"]);
        }

        if (call.Description is not null)
        {
            args.AddRange(["/d", call.Description]);
        }

        if (call.DescriptionUrl is not null)
        {
            args.AddRange(["/du", call.DescriptionUrl]);
        }

        args.AddRange(call.Files);
        return new ProcessSpec(signToolPath, args, Timeout: TimeSpan.FromMinutes(30), SecretArguments: secrets);
    }

    public static ProcessSpec BuildVerify(string signToolPath, string file) =>
        new(signToolPath, ["verify", "/pa", "/v", file], Timeout: TimeSpan.FromMinutes(5));

    /// <summary>Sucht signtool.exe im Windows SDK (wie Find-SignTool in build/sign.ps1).</summary>
    public static string? Find(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64";
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Kits", "10", "bin");
        if (!Directory.Exists(root))
        {
            return null;
        }

        return Directory.EnumerateFiles(root, "signtool.exe", SearchOption.AllDirectories)
            .Where(p => p.Contains(Path.DirectorySeparatorChar + arch + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }
}

/// <summary>Fallback ohne signtool: Set-AuthenticodeSignature (gleichwertig für WDAC/AppLocker).</summary>
public static class PowerShellSignCommand
{
    public static ProcessSpec Build(string pwshPath, string thumbprint, string? timestampUrl, IReadOnlyList<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var script = """
            param([string]$Thumbprint, [string]$TimestampUrl, [string[]]$Files)
            $ErrorActionPreference = 'Stop'
            $cert = $null
            foreach ($store in 'Cert:\LocalMachine\My', 'Cert:\CurrentUser\My') {
                $cert = Get-Item -LiteralPath (Join-Path $store $Thumbprint) -ErrorAction SilentlyContinue
                if ($cert) { break }
            }
            if (-not $cert) { throw "Zertifikat $Thumbprint nicht gefunden." }
            if (-not $cert.HasPrivateKey) { throw "Zertifikat $Thumbprint hat keinen privaten Schlüssel." }
            foreach ($f in $Files) {
                $p = @{ FilePath = $f; Certificate = $cert; HashAlgorithm = 'SHA256' }
                if ($TimestampUrl) { $p.TimestampServer = $TimestampUrl }
                $r = Set-AuthenticodeSignature @p
                if ($r.Status -ne 'Valid') { throw "Signatur für $f ungültig: $($r.Status) $($r.StatusMessage)" }
                Write-Output "Signed: $f"
            }
            """;
        static string Q(string v) => "'" + v.Replace("'", "''", StringComparison.Ordinal) + "'";
        var command = "& {" + script + "} -Thumbprint " + Q(thumbprint) + " -TimestampUrl " + Q(timestampUrl ?? string.Empty)
            + " -Files @(" + string.Join(',', files.Select(Q)) + ")";
        var args = new List<string> { "-NoProfile", "-NonInteractive", "-Command", command };
        return new ProcessSpec(pwshPath, args, Timeout: TimeSpan.FromMinutes(30));
    }
}
