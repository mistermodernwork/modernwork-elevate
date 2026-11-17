using System.Globalization;
using System.Text.RegularExpressions;
using AppForge.Core.Domain;

namespace AppForge.Core.Versions;

/// <summary>
/// Tolerante Versionsnummer: numerische Teile werden numerisch, Suffixe (beta, rc) lexikalisch verglichen.
/// Vorabversionen (mit Suffix) sind kleiner als dieselbe Version ohne Suffix.
/// </summary>
public sealed partial class SoftwareVersion : IComparable<SoftwareVersion>, IEquatable<SoftwareVersion>
{
    private readonly long[] _numbers;
    private readonly string _suffix;

    private SoftwareVersion(string original, long[] numbers, string suffix)
    {
        Original = original;
        _numbers = numbers;
        _suffix = suffix;
    }

    public string Original { get; }

    public bool IsPrerelease => _suffix.Length > 0;

    public static SoftwareVersion Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var trimmed = value.Trim().TrimStart('v', 'V');
        var match = Leading().Match(trimmed);
        var numbers = match.Success
            ? match.Value.Split('.', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => long.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0).ToArray()
            : [];
        var suffix = match.Success ? trimmed[match.Length..].TrimStart('-', '+', '.', '_', ' ') : trimmed;
        return new SoftwareVersion(value, numbers, suffix.ToLowerInvariant());
    }

    public static bool TryParse(string? value, out SoftwareVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value) || !char.IsDigit(value.Trim().TrimStart('v', 'V').FirstOrDefault()))
        {
            return false;
        }

        version = Parse(value);
        return true;
    }

    public int CompareTo(SoftwareVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var length = Math.Max(_numbers.Length, other._numbers.Length);
        for (var i = 0; i < length; i++)
        {
            var a = i < _numbers.Length ? _numbers[i] : 0;
            var b = i < other._numbers.Length ? other._numbers[i] : 0;
            if (a != b)
            {
                return a.CompareTo(b);
            }
        }

        if (_suffix.Length == 0 && other._suffix.Length == 0)
        {
            return 0;
        }

        if (_suffix.Length == 0)
        {
            return 1;
        }

        if (other._suffix.Length == 0)
        {
            return -1;
        }

        return string.CompareOrdinal(_suffix, other._suffix);
    }

    public bool Equals(SoftwareVersion? other) => other is not null && CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is SoftwareVersion v && Equals(v);

    public override int GetHashCode()
    {
        var trimmed = _numbers.Reverse().SkipWhile(n => n == 0).Reverse();
        var hash = new HashCode();
        foreach (var n in trimmed)
        {
            hash.Add(n);
        }

        hash.Add(_suffix);
        return hash.ToHashCode();
    }

    public override string ToString() => Original;

    public static bool operator <(SoftwareVersion a, SoftwareVersion b) => a.CompareTo(b) < 0;

    public static bool operator >(SoftwareVersion a, SoftwareVersion b) => a.CompareTo(b) > 0;

    public static bool operator <=(SoftwareVersion a, SoftwareVersion b) => a.CompareTo(b) <= 0;

    public static bool operator >=(SoftwareVersion a, SoftwareVersion b) => a.CompareTo(b) >= 0;

    public static bool operator ==(SoftwareVersion? a, SoftwareVersion? b) => a is null ? b is null : a.Equals(b);

    public static bool operator !=(SoftwareVersion? a, SoftwareVersion? b) => !(a == b);

    [GeneratedRegex(@"^\d+(\.\d+)*")]
    private static partial Regex Leading();
}

/// <summary>Ergebnis der Versionsauflösung: welche Version für welchen Ring gebaut wird.</summary>
public sealed record ResolvedVersion(string Version, DeploymentRing? Ring);

/// <summary>Wählt aus den verfügbaren Versionen einer Quelle die zu bauenden aus (Plan 4.2).</summary>
public static class VersionResolver
{
    public static IReadOnlyList<ResolvedVersion> Resolve(
        IEnumerable<string> available,
        VersionMode mode,
        IReadOnlyList<string> selected,
        IReadOnlyDictionary<DeploymentRing, int>? ringOffsets = null,
        bool includePrerelease = false)
    {
        var sorted = available
            .Where(v => SoftwareVersion.TryParse(v, out _))
            .Select(SoftwareVersion.Parse)
            .Where(v => includePrerelease || !v.IsPrerelease)
            .Distinct()
            .OrderByDescending(v => v)
            .ToList();

        if (sorted.Count == 0)
        {
            return [];
        }

        switch (mode)
        {
            case VersionMode.Latest:
                return [new ResolvedVersion(sorted[0].Original, null)];

            case VersionMode.Pinned:
                {
                    var pin = selected.FirstOrDefault();
                    var match = pin is null ? null : FindByRule(sorted, pin);
                    return match is null ? [] : [new ResolvedVersion(match.Original, null)];
                }

            case VersionMode.Multiple:
                return selected
                    .Select(rule => FindByRule(sorted, rule))
                    .Where(v => v is not null)
                    .Distinct()
                    .Select(v => new ResolvedVersion(v!.Original, null))
                    .ToList();

            case VersionMode.Ring:
                {
                    var offsets = ringOffsets ?? new Dictionary<DeploymentRing, int> { [DeploymentRing.Pilot] = 0, [DeploymentRing.Production] = 1 };
                    return offsets
                        .OrderBy(kv => kv.Key)
                        .Where(kv => kv.Value >= 0 && kv.Value < sorted.Count)
                        .Select(kv => new ResolvedVersion(sorted[kv.Value].Original, kv.Key))
                        .ToList();
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }

    /// <summary>
    /// Regeln: exakte Version ("24.08"), "latest", "latest-N" oder Präfix mit Platzhalter ("128.*" = neueste 128er).
    /// </summary>
    private static SoftwareVersion? FindByRule(List<SoftwareVersion> sortedDescending, string rule)
    {
        var r = rule.Trim();
        if (r.Equals("latest", StringComparison.OrdinalIgnoreCase))
        {
            return sortedDescending[0];
        }

        if (r.StartsWith("latest-", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(r["latest-".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var offset))
        {
            return offset < sortedDescending.Count ? sortedDescending[offset] : null;
        }

        if (r.EndsWith(".*", StringComparison.Ordinal))
        {
            var prefix = r[..^1];
            return sortedDescending.FirstOrDefault(v => v.Original.TrimStart('v', 'V').StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        var wanted = SoftwareVersion.Parse(r);
        return sortedDescending.FirstOrDefault(v => v == wanted);
    }
}
