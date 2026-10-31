using System.Globalization;
using System.Text.RegularExpressions;

namespace Tamp.SpfxCli;

/// <summary>
/// A semantic version (2.0.0) with precedence comparison, including pre-release identifiers, so
/// <c>0.1.0-pre.3 &lt; 0.1.0-pre.10 &lt; 0.1.0</c>. Build metadata is kept but ignored for ordering.
/// </summary>
public sealed record SemVer : IComparable<SemVer>
{
    private static readonly Regex Pattern = new(
        @"^v?(?<maj>0|[1-9]\d*)\.(?<min>0|[1-9]\d*)\.(?<pat>0|[1-9]\d*)(?:-(?<pre>[0-9A-Za-z\-]+(?:\.[0-9A-Za-z\-]+)*))?(?:\+(?<meta>[0-9A-Za-z\-]+(?:\.[0-9A-Za-z\-]+)*))?$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);

    /// <summary>Major.</summary>
    public required int Major { get; init; }

    /// <summary>Minor.</summary>
    public required int Minor { get; init; }

    /// <summary>Patch.</summary>
    public required int Patch { get; init; }

    /// <summary>Pre-release identifiers (empty for a release).</summary>
    public IReadOnlyList<string> PreRelease { get; init; } = Array.Empty<string>();

    /// <summary>Build metadata, or null.</summary>
    public string? Metadata { get; init; }

    /// <summary>Parse, or null when <paramref name="value"/> isn't a semantic version.</summary>
    public static SemVer? TryParse(string? value)
    {
        if (value is null) return null;
        var m = Pattern.Match(value.Trim());
        if (!m.Success) return null;
        if (!int.TryParse(m.Groups["maj"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var maj)
            || !int.TryParse(m.Groups["min"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var min)
            || !int.TryParse(m.Groups["pat"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var pat))
            return null;
        return new SemVer
        {
            Major = maj, Minor = min, Patch = pat,
            PreRelease = m.Groups["pre"].Success ? m.Groups["pre"].Value.Split('.') : Array.Empty<string>(),
            Metadata = m.Groups["meta"].Success ? m.Groups["meta"].Value : null,
        };
    }

    /// <inheritdoc />
    public int CompareTo(SemVer? other)
    {
        if (other is null) return 1;
        var c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;

        if (PreRelease.Count == 0) return other.PreRelease.Count == 0 ? 0 : 1;
        if (other.PreRelease.Count == 0) return -1;

        for (var i = 0; i < Math.Min(PreRelease.Count, other.PreRelease.Count); i++)
        {
            var a = PreRelease[i];
            var b = other.PreRelease[i];
            var aNum = long.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out var an);
            var bNum = long.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out var bn);
            c = (aNum, bNum) switch
            {
                (true, true) => an.CompareTo(bn),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(a, b),
            };
            if (c != 0) return Math.Sign(c);
        }
        return PreRelease.Count.CompareTo(other.PreRelease.Count);
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"{Major}.{Minor}.{Patch}" + (PreRelease.Count > 0 ? "-" + string.Join('.', PreRelease) : "") + (Metadata is null ? "" : "+" + Metadata);

    /// <summary>Less than.</summary>
    public static bool operator <(SemVer a, SemVer b) => a.CompareTo(b) < 0;

    /// <summary>Greater than.</summary>
    public static bool operator >(SemVer a, SemVer b) => a.CompareTo(b) > 0;

    /// <summary>Less than or equal.</summary>
    public static bool operator <=(SemVer a, SemVer b) => a.CompareTo(b) <= 0;

    /// <summary>Greater than or equal.</summary>
    public static bool operator >=(SemVer a, SemVer b) => a.CompareTo(b) >= 0;
}
