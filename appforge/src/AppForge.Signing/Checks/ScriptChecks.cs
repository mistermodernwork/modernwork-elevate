using System.Text;
using System.Text.RegularExpressions;

namespace AppForge.Signing.Checks;

public enum CheckSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record CheckFinding(string Check, CheckSeverity Severity, string Message, string? File = null, int? Line = null);

/// <summary>Ergebnis aller Vorprüfungen (Plan 4.1, Schritt 2) – wird als Signaturbericht gespeichert.</summary>
public sealed record PreCheckReport(IReadOnlyList<CheckFinding> Findings)
{
    public bool Passed => Findings.All(f => f.Severity != CheckSeverity.Error);
}

/// <summary>UTF-8 mit BOM normalisieren – sonst bricht die Signatur bei Umlauten in Windows PowerShell 5.1.</summary>
public static class ScriptEncoding
{
    public static readonly string[] TextExtensions = [".ps1", ".psm1", ".psd1", ".ps1xml", ".vbs", ".js"];

    public static bool IsScript(string path) => TextExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>Gibt true zurück, wenn die Datei geändert wurde.</summary>
    public static bool NormalizeToUtf8Bom(string path)
    {
        var bytes = File.ReadAllBytes(path);
        string text;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return false;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            text = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            text = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }
        else
        {
            var strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
            try
            {
                text = strict.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                // Altlast: Windows-1252 (typisch für Skripte aus Windows PowerShell ISE).
                text = Encoding.Latin1.GetString(bytes);
            }
        }

        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return true;
    }
}

/// <summary>Sucht nach Klartext-Secrets (Tokens, Passwörter, Schlüssel) in Skripten.</summary>
public static partial class SecretScanner
{
    private static readonly (string Name, Regex Pattern)[] Patterns =
    [
        ("Private Key", PrivateKey()),
        ("GitHub Token", GitHubToken()),
        ("Azure Storage Key", StorageKey()),
        ("Azure SAS", Sas()),
        ("Client Secret", ClientSecret()),
        ("Klartext-Passwort", PlainPassword()),
        ("AWS Access Key", AwsKey()),
        ("Slack/Teams Webhook", Webhook()),
        ("JWT", Jwt()),
    ];

    public static IReadOnlyList<CheckFinding> Scan(string file)
    {
        var findings = new List<CheckFinding>();
        var lines = File.ReadAllLines(file);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.TrimStart().StartsWith("# SIG #", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var (name, pattern) in Patterns)
            {
                if (pattern.IsMatch(line))
                {
                    findings.Add(new CheckFinding("SecretScan", CheckSeverity.Error, $"Mögliches Secret gefunden ({name}).", Path.GetFileName(file), i + 1));
                }
            }
        }

        return findings;
    }

    [GeneratedRegex("-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----")]
    private static partial Regex PrivateKey();

    [GeneratedRegex(@"\b(ghp|gho|ghu|ghs|ghr|github_pat)_[A-Za-z0-9_]{20,}\b")]
    private static partial Regex GitHubToken();

    [GeneratedRegex(@"AccountKey=[A-Za-z0-9+/]{40,}={0,2}")]
    private static partial Regex StorageKey();

    [GeneratedRegex(@"[?&]sig=[A-Za-z0-9%+/]{30,}")]
    private static partial Regex Sas();

    [GeneratedRegex(@"(?i)(client_?secret|clientsecret)\s*[=:]\s*['""][^'""\s]{16,}['""]")]
    private static partial Regex ClientSecret();

    [GeneratedRegex(@"(?i)(password|passwort|pwd)\s*=\s*['""][^'""$\s]{4,}['""]")]
    private static partial Regex PlainPassword();

    [GeneratedRegex(@"\bAKIA[0-9A-Z]{16}\b")]
    private static partial Regex AwsKey();

    [GeneratedRegex(@"https://[a-z0-9.-]*(webhook\.office\.com|hooks\.slack\.com)/[^\s'""]+")]
    private static partial Regex Webhook();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{10,}\.eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b")]
    private static partial Regex Jwt();
}

/// <summary>Einfache, schnelle Regeln zusätzlich zu PSScriptAnalyzer (laufen auch ohne pwsh).</summary>
public static partial class ScriptRules
{
    public static IReadOnlyList<CheckFinding> Check(string file)
    {
        var findings = new List<CheckFinding>();
        var lines = File.ReadAllLines(file);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = StripComment(lines[i]);
            if (InvokeExpression().IsMatch(line))
            {
                findings.Add(new("ScriptRules", CheckSeverity.Error, "Invoke-Expression/iex ist nicht erlaubt.", Path.GetFileName(file), i + 1));
            }

            if (DownloadExecute().IsMatch(line))
            {
                findings.Add(new("ScriptRules", CheckSeverity.Error, "Nachladen und Ausführen von Code aus dem Netz ist nicht erlaubt.", Path.GetFileName(file), i + 1));
            }

            if (ConvertToSecure().IsMatch(line))
            {
                findings.Add(new("ScriptRules", CheckSeverity.Error, "ConvertTo-SecureString -AsPlainText mit Literal – Klartext-Passwort im Skript.", Path.GetFileName(file), i + 1));
            }

            if (ExecutionPolicyBypass().IsMatch(line))
            {
                findings.Add(new("ScriptRules", CheckSeverity.Warning, "Ausführungsrichtlinie wird umgangen.", Path.GetFileName(file), i + 1));
            }
        }

        return findings;
    }

    private static string StripComment(string line)
    {
        var idx = line.IndexOf('#', StringComparison.Ordinal);
        return idx >= 0 ? line[..idx] : line;
    }

    [GeneratedRegex(@"(?i)(\bInvoke-Expression\b|(^|[\s;|(])iex(\s|\(|$))")]
    private static partial Regex InvokeExpression();

    [GeneratedRegex(@"(?i)(DownloadString|Invoke-RestMethod|irm|iwr|Invoke-WebRequest)[^|]*\|\s*(iex|Invoke-Expression)")]
    private static partial Regex DownloadExecute();

    [GeneratedRegex(@"(?i)ConvertTo-SecureString\s+(-String\s+)?['""][^'""]+['""]\s+-AsPlainText")]
    private static partial Regex ConvertToSecure();

    [GeneratedRegex(@"(?i)-ExecutionPolicy\s+Bypass")]
    private static partial Regex ExecutionPolicyBypass();
}

/// <summary>Anforderungen von Intune an Remediations, Platform- und Compliance-Skripte (Plan 4.1, F2).</summary>
public static partial class IntuneScriptRules
{
    public const int MaxRemediationBytes = 200 * 1024;

    public static IReadOnlyList<CheckFinding> CheckRemediationPair(string detectionFile, string? remediationFile)
    {
        var findings = new List<CheckFinding>();
        findings.AddRange(CheckCommon(detectionFile, "Detection"));
        var detection = File.ReadAllText(detectionFile);
        if (!ExitOne().IsMatch(detection))
        {
            findings.Add(new("IntuneRules", CheckSeverity.Error, "Detection-Skript muss mit 'exit 1' melden, dass eine Korrektur nötig ist.", Path.GetFileName(detectionFile)));
        }

        if (!ExitZero().IsMatch(detection))
        {
            findings.Add(new("IntuneRules", CheckSeverity.Warning, "Detection-Skript sollte mit 'exit 0' den konformen Zustand melden.", Path.GetFileName(detectionFile)));
        }

        if (remediationFile is not null)
        {
            findings.AddRange(CheckCommon(remediationFile, "Remediation"));
        }

        return findings;
    }

    public static IReadOnlyList<CheckFinding> CheckCommon(string file, string role)
    {
        var findings = new List<CheckFinding>();
        var info = new FileInfo(file);
        if (info.Length > MaxRemediationBytes)
        {
            findings.Add(new("IntuneRules", CheckSeverity.Error, $"{role}-Skript ist größer als 200 KB.", info.Name));
        }

        var text = File.ReadAllText(file);
        if (Interactive().IsMatch(text))
        {
            findings.Add(new("IntuneRules", CheckSeverity.Error, $"{role}-Skript ist interaktiv (Read-Host/Get-Credential/Pause) – läuft unbeaufsichtigt als SYSTEM.", info.Name));
        }

        if (!Output().IsMatch(text))
        {
            findings.Add(new("IntuneRules", CheckSeverity.Info, $"{role}-Skript schreibt nichts nach STDOUT – Intune zeigt dann keine Ausgabe an.", info.Name));
        }

        return findings;
    }

    [GeneratedRegex(@"(?i)\bexit\s+1\b")]
    private static partial Regex ExitOne();

    [GeneratedRegex(@"(?im)\bexit\s+0\b")]
    private static partial Regex ExitZero();

    [GeneratedRegex(@"(?i)\b(Read-Host|Get-Credential|pause)\b")]
    private static partial Regex Interactive();

    [GeneratedRegex(@"(?i)\b(Write-Output|Write-Host|echo)\b")]
    private static partial Regex Output();
}
