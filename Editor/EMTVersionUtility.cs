using System;
using System.Text.RegularExpressions;

namespace EMT.Packages.Editor
{
    /// <summary>Semantic Versioning 2.0.0 value (a leading "v" is tolerated when parsing).</summary>
    public readonly struct EMTSemVer : IComparable<EMTSemVer>, IEquatable<EMTSemVer>
    {
        public readonly int Major;
        public readonly int Minor;
        public readonly int Patch;
        public readonly string PreRelease; // empty when stable
        public readonly string Build;      // ignored in comparisons

        public EMTSemVer(int major, int minor, int patch, string preRelease = "", string build = "")
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            PreRelease = preRelease ?? string.Empty;
            Build = build ?? string.Empty;
        }

        public bool IsPreRelease => !string.IsNullOrEmpty(PreRelease);

        public int CompareTo(EMTSemVer other) => EMTVersionUtility.Compare(this, other);
        public bool Equals(EMTSemVer other) => CompareTo(other) == 0;
        public override bool Equals(object obj) => obj is EMTSemVer v && Equals(v);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = Major;
                h = (h * 397) ^ Minor;
                h = (h * 397) ^ Patch;
                h = (h * 397) ^ (PreRelease ?? string.Empty).GetHashCode();
                return h;
            }
        }

        public override string ToString()
        {
            string s = Major + "." + Minor + "." + Patch;
            if (!string.IsNullOrEmpty(PreRelease)) s += "-" + PreRelease;
            if (!string.IsNullOrEmpty(Build)) s += "+" + Build;
            return s;
        }
    }

    /// <summary>Parsing and comparison of SemVer strings. Never compares versions as plain strings.</summary>
    public static class EMTVersionUtility
    {
        private const string Ident = @"(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)";

        private static readonly Regex Pattern = new Regex(
            @"^[vV]?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)" +
            @"(?:-(" + Ident + @"(?:\." + Ident + @")*))?" +
            @"(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$",
            RegexOptions.CultureInvariant);

        public static bool TryParse(string text, out EMTSemVer version)
        {
            version = default;
            if (string.IsNullOrWhiteSpace(text)) return false;

            Match m = Pattern.Match(text.Trim());
            if (!m.Success) return false;

            if (!int.TryParse(m.Groups[1].Value, out int major) ||
                !int.TryParse(m.Groups[2].Value, out int minor) ||
                !int.TryParse(m.Groups[3].Value, out int patch))
                return false;

            version = new EMTSemVer(major, minor, patch, m.Groups[4].Value, m.Groups[5].Value);
            return true;
        }

        /// <summary>Returns -1 if a &lt; b, 0 if equal, 1 if a &gt; b (SemVer 2.0.0 precedence).</summary>
        public static int Compare(EMTSemVer a, EMTSemVer b)
        {
            int c = a.Major.CompareTo(b.Major);
            if (c != 0) return Math.Sign(c);
            c = a.Minor.CompareTo(b.Minor);
            if (c != 0) return Math.Sign(c);
            c = a.Patch.CompareTo(b.Patch);
            if (c != 0) return Math.Sign(c);
            return ComparePreRelease(a.PreRelease, b.PreRelease);
        }

        /// <summary>Parses both strings and compares them. Returns false when either is invalid.</summary>
        public static bool TryCompare(string a, string b, out int result)
        {
            result = 0;
            if (!TryParse(a, out EMTSemVer va) || !TryParse(b, out EMTSemVer vb)) return false;
            result = Compare(va, vb);
            return true;
        }

        private static int ComparePreRelease(string a, string b)
        {
            bool aEmpty = string.IsNullOrEmpty(a);
            bool bEmpty = string.IsNullOrEmpty(b);
            if (aEmpty && bEmpty) return 0;
            if (aEmpty) return 1;   // a stable release outranks any pre-release
            if (bEmpty) return -1;

            string[] pa = a.Split('.');
            string[] pb = b.Split('.');
            int n = Math.Min(pa.Length, pb.Length);

            for (int i = 0; i < n; i++)
            {
                bool na = IsNumeric(pa[i]);
                bool nb = IsNumeric(pb[i]);
                int c;
                if (na && nb) c = CompareNumeric(pa[i], pb[i]);
                else if (na) c = -1;  // numeric identifiers rank lower than alphanumeric
                else if (nb) c = 1;
                else c = string.CompareOrdinal(pa[i], pb[i]);

                if (c != 0) return Math.Sign(c);
            }

            return Math.Sign(pa.Length.CompareTo(pb.Length));
        }

        private static bool IsNumeric(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
                if (s[i] < '0' || s[i] > '9') return false;
            return true;
        }

        // Numeric identifiers have no leading zeros, so length then ordinal comparison is exact (no overflow).
        private static int CompareNumeric(string a, string b)
        {
            if (a.Length != b.Length) return a.Length.CompareTo(b.Length);
            return string.CompareOrdinal(a, b);
        }
    }
}
