namespace QrMaster.Services;

/// <summary>Minimal SemVer 2.0 parser/comparer (major.minor.patch[-pre.release][+build]).</summary>
internal sealed class SemVer : IComparable<SemVer>, IEquatable<SemVer>
{
    private readonly string[] _pre;

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    /// <summary>First pre-release identifier (e.g. "dev" in 1.2.0-dev.3), or null for a stable release.</summary>
    public string? Label => _pre.Length > 0 ? _pre[0] : null;

    private SemVer(int major, int minor, int patch, string[] pre)
    {
        Major = major; Minor = minor; Patch = patch; _pre = pre;
    }

    public static bool TryParse(string? text, out SemVer version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var s = text.Trim();
        if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];

        var plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];

        string[] pre = [];
        var dash = s.IndexOf('-');
        if (dash >= 0)
        {
            pre = s[(dash + 1)..].Split('.');
            if (pre.Any(string.IsNullOrEmpty)) return false;
            s = s[..dash];
        }

        var parts = s.Split('.');
        if (parts.Length != 3
            || !int.TryParse(parts[0], out var ma) || ma < 0
            || !int.TryParse(parts[1], out var mi) || mi < 0
            || !int.TryParse(parts[2], out var pa) || pa < 0)
            return false;

        version = new SemVer(ma, mi, pa, pre);
        return true;
    }

    public int CompareTo(SemVer? other)
    {
        if (other is null) return 1;
        var c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;

        // A version without pre-release has higher precedence.
        if (_pre.Length == 0) return other._pre.Length == 0 ? 0 : 1;
        if (other._pre.Length == 0) return -1;

        for (var i = 0; i < Math.Min(_pre.Length, other._pre.Length); i++)
        {
            var a = _pre[i];
            var b = other._pre[i];
            var aNum = int.TryParse(a, out var an);
            var bNum = int.TryParse(b, out var bn);
            c = (aNum, bNum) switch
            {
                (true, true) => an.CompareTo(bn),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(a, b)
            };
            if (c != 0) return c;
        }
        return _pre.Length.CompareTo(other._pre.Length);
    }

    public bool Equals(SemVer? other) => other is not null && CompareTo(other) == 0;
    public override bool Equals(object? obj) => obj is SemVer o && Equals(o);
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, string.Join('.', _pre));

    public override string ToString() =>
        $"{Major}.{Minor}.{Patch}" + (_pre.Length > 0 ? "-" + string.Join('.', _pre) : "");

    public static bool operator >(SemVer a, SemVer b) => a.CompareTo(b) > 0;
    public static bool operator <(SemVer a, SemVer b) => a.CompareTo(b) < 0;
}
