using AppForge.Core.Processes;

namespace AppForge.Signing.Engines;

/// <summary>Parameter eines jsign-Aufrufs (unabhängig vom Provider).</summary>
public sealed record JsignCall
{
    public required string StoreType { get; init; }

    public string? Keystore { get; init; }

    public string? Alias { get; init; }

    /// <summary>Wert für <c>--storepass</c>; wird nie auf der Kommandozeile übergeben, sondern über <c>env:</c>.</summary>
    public string? StorePassword { get; init; }

    public string? KeyPassword { get; init; }

    public string? CertFile { get; init; }

    public string? TimestampUrl { get; init; }

    public int TimestampRetries { get; init; } = 3;

    public string? Name { get; init; }

    public string? Url { get; init; }

    public bool Replace { get; init; } = true;

    public string? ProxyUrl { get; init; }

    public required IReadOnlyList<string> Files { get; init; }
}

/// <summary>
/// Baut die jsign-Kommandozeile. Passwörter/Tokens gehen über Umgebungsvariablen
/// (<c>--storepass env:JSIGN_STOREPASS</c>), damit sie nicht in der Prozessliste erscheinen.
/// </summary>
public static class JsignCommand
{
    public const string StorePassVariable = "APPFORGE_JSIGN_STOREPASS";
    public const string KeyPassVariable = "APPFORGE_JSIGN_KEYPASS";

    public static ProcessSpec Build(string javaPath, string jsignPath, JsignCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (call.Files.Count == 0)
        {
            throw new ArgumentException("Keine Dateien zum Signieren.", nameof(call));
        }

        var args = new List<string>();
        var fileName = jsignPath;
        if (jsignPath.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
        {
            fileName = javaPath;
            args.Add("-jar");
            args.Add(jsignPath);
        }

        args.Add("--storetype");
        args.Add(call.StoreType);
        if (call.Keystore is not null)
        {
            args.Add("--keystore");
            args.Add(call.Keystore);
        }

        var env = new Dictionary<string, string>();
        if (!OperatingSystem.IsWindows())
        {
            // Ohne UTF-8-Locale findet die JVM Dateinamen mit Umlauten nicht.
            env["LC_ALL"] = "C.UTF-8";
            env["LANG"] = "C.UTF-8";
        }

        if (call.StorePassword is not null)
        {
            args.Add("--storepass");
            args.Add("env:" + StorePassVariable);
            env[StorePassVariable] = call.StorePassword;
        }

        if (call.KeyPassword is not null)
        {
            args.Add("--keypass");
            args.Add("env:" + KeyPassVariable);
            env[KeyPassVariable] = call.KeyPassword;
        }

        if (call.Alias is not null)
        {
            args.Add("--alias");
            args.Add(call.Alias);
        }

        if (call.CertFile is not null)
        {
            args.Add("--certfile");
            args.Add(call.CertFile);
        }

        args.Add("--alg");
        args.Add("SHA-256");

        if (!string.IsNullOrWhiteSpace(call.TimestampUrl))
        {
            args.Add("--tsaurl");
            args.Add(call.TimestampUrl);
            args.Add("--tsmode");
            args.Add("RFC3161");
            args.Add("--tsretries");
            args.Add(call.TimestampRetries.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (call.Name is not null)
        {
            args.Add("--name");
            args.Add(call.Name);
        }

        if (call.Url is not null)
        {
            args.Add("--url");
            args.Add(call.Url);
        }

        if (call.ProxyUrl is not null)
        {
            args.Add("--proxyUrl");
            args.Add(call.ProxyUrl);
        }

        if (call.Replace)
        {
            args.Add("--replace");
        }

        // PowerShell-Skripte normalisiert die Plattform vorher auf UTF-8 mit BOM.
        args.Add("--encoding");
        args.Add("UTF-8");
        args.AddRange(call.Files);
        return new ProcessSpec(fileName, args, Environment: env, Timeout: TimeSpan.FromMinutes(30));
    }
}
