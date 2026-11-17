using System.Text.Json;
using AppForge.Core.Abstractions;
using AppForge.Core.Processes;
using Azure.Storage.Blobs;

namespace AppForge.Signing.Checks;

/// <summary>PSScriptAnalyzer über pwsh (läuft auf Linux im Job-Image und auf Windows).</summary>
public sealed class PsScriptAnalyzer(IProcessRunner runner, SigningOptions options, string? settingsFile = null) : IScriptAnalyzer
{
    public async Task<IReadOnlyList<AnalyzerFinding>> AnalyzeAsync(string scriptPath, CancellationToken ct = default)
    {
        var settings = settingsFile is null ? string.Empty : $" -Settings '{settingsFile.Replace("'", "''", StringComparison.Ordinal)}'";
        var command =
            "$ErrorActionPreference='Stop'; Import-Module PSScriptAnalyzer; " +
            $"$r = @(Invoke-ScriptAnalyzer -Path '{scriptPath.Replace("'", "''", StringComparison.Ordinal)}'{settings}); " +
            "$r | Select-Object RuleName,@{n='Severity';e={$_.Severity.ToString()}},Line,Message | ConvertTo-Json -Depth 3 -AsArray";
        var result = await runner.RunAsync(new ProcessSpec(options.PowerShellPath, ["-NoProfile", "-NonInteractive", "-Command", command], Timeout: TimeSpan.FromMinutes(5)), ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException("PSScriptAnalyzer konnte nicht ausgeführt werden: " + result.StandardError.Trim());
        }

        return Parse(result.StandardOutput);
    }

    internal static IReadOnlyList<AnalyzerFinding> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray().Select(e => new AnalyzerFinding(
            e.GetProperty("RuleName").GetString() ?? string.Empty,
            e.GetProperty("Severity").GetString() ?? "Information",
            e.TryGetProperty("Line", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetInt32() : 0,
            e.GetProperty("Message").GetString() ?? string.Empty)).ToList();
    }
}

/// <summary>
/// Microsoft Defender for Storage – Malware Scanning beim Upload (Standard in der Cloud).
/// Wartet auf den Blob-Index-Tag „Malware Scanning scan result“.
/// </summary>
public sealed class DefenderForStorageScanner(BlobContainerClient container, TimeSpan? timeout = null, TimeSpan? pollInterval = null) : IMalwareScanner
{
    public const string ResultTag = "Malware Scanning scan result";
    public const string CleanValue = "No threats found";

    /// <param name="path">Blob-Name (Schlüssel im Artefaktspeicher).</param>
    public async Task<ScanResult> ScanAsync(string path, CancellationToken ct = default)
    {
        var blob = container.GetBlobClient(path);
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromMinutes(10));
        while (DateTimeOffset.UtcNow < deadline)
        {
            var tags = (await blob.GetTagsAsync(cancellationToken: ct).ConfigureAwait(false)).Value.Tags;
            if (tags.TryGetValue(ResultTag, out var value))
            {
                return Interpret(value);
            }

            await Task.Delay(pollInterval ?? TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
        }

        return new ScanResult(false, "Defender for Storage", "Kein Scan-Ergebnis innerhalb der Wartezeit – Datei wird nicht weiterverarbeitet.");
    }

    internal static ScanResult Interpret(string value) =>
        string.Equals(value, CleanValue, StringComparison.OrdinalIgnoreCase)
            ? new ScanResult(true, "Defender for Storage", value)
            : new ScanResult(false, "Defender for Storage", value);
}

/// <summary>Microsoft Defender Antivirus per MpCmdRun (nur Windows-Worker).</summary>
public sealed class DefenderCliScanner(IProcessRunner runner, string? mpCmdRunPath = null) : IMalwareScanner
{
    public static ProcessSpec BuildSpec(string mpCmdRun, string path) =>
        new(mpCmdRun, ["-Scan", "-ScanType", "3", "-File", path, "-DisableRemediation"], Timeout: TimeSpan.FromMinutes(15));

    public async Task<ScanResult> ScanAsync(string path, CancellationToken ct = default)
    {
        var exe = mpCmdRunPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender", "MpCmdRun.exe");
        var result = await runner.RunAsync(BuildSpec(exe, path), ct).ConfigureAwait(false);

        // 0 = keine Bedrohung, 2 = Bedrohung gefunden.
        return result.ExitCode switch
        {
            0 => new ScanResult(true, "Defender CLI", result.StandardOutput.Trim()),
            2 => new ScanResult(false, "Defender CLI", "Bedrohung gefunden: " + result.StandardOutput.Trim()),
            _ => new ScanResult(false, "Defender CLI", $"Scan fehlgeschlagen ({result.ExitCode}): {result.StandardError.Trim()}"),
        };
    }
}

/// <summary>Nur Entwicklung/Tests: meldet jede Datei als sauber und sagt das deutlich.</summary>
public sealed class DevelopmentNoScanScanner : IMalwareScanner
{
    public Task<ScanResult> ScanAsync(string path, CancellationToken ct = default) =>
        Task.FromResult(new ScanResult(true, "none", "Kein Malware-Scanner konfiguriert (nur Entwicklung)."));
}
