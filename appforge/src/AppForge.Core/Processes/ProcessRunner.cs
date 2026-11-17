using System.Diagnostics;
using System.Text;

namespace AppForge.Core.Processes;

public sealed record ProcessSpec(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string>? Environment = null,
    TimeSpan? Timeout = null,
    IReadOnlyCollection<string>? SecretArguments = null)
{
    /// <summary>Anzeige für Logs – Secrets werden maskiert.</summary>
    public string ToDisplayString()
    {
        var secrets = SecretArguments ?? [];
        return FileName + " " + string.Join(' ', Arguments.Select(a => secrets.Contains(a) ? "***" : CommandLine.Quote(a)));
    }
}

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut);

/// <summary>Abstraktion für externe Werkzeuge (signtool, sign, IntuneWinAppUtil, pwsh …) – in Tests ersetzbar.</summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct = default);
}

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var psi = new ProcessStartInfo(spec.FileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = spec.WorkingDirectory ?? string.Empty,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in spec.Arguments)
        {
            psi.ArgumentList.Add(a);
        }

        foreach (var (k, v) in spec.Environment ?? new Dictionary<string, string>())
        {
            psi.Environment[k] = v;
        }

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { stdout.AppendLine(e.Data); } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { stderr.AppendLine(e.Data); } };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = new CancellationTokenSource(spec.Timeout ?? TimeSpan.FromMinutes(30));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // bereits beendet
            }

            if (ct.IsCancellationRequested)
            {
                throw;
            }

            return new ProcessResult(-1, stdout.ToString(), stderr.ToString(), true);
        }

        process.WaitForExit();
        return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString(), false);
    }
}

public static class CommandLine
{
    /// <summary>Quoting nach Windows-Regeln (CommandLineToArgvW) für Log-Ausgaben und Install-Kommandos.</summary>
    public static string Quote(string arg)
    {
        ArgumentNullException.ThrowIfNull(arg);
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '"']) < 0)
        {
            return arg;
        }

        var sb = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                sb.Append('\\', (backslashes * 2) + 1).Append('"');
            }
            else
            {
                sb.Append('\\', backslashes).Append(c);
            }

            backslashes = 0;
        }

        sb.Append('\\', backslashes * 2).Append('"');
        return sb.ToString();
    }
}
