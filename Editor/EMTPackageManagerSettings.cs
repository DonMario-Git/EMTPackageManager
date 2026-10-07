using System;
using UnityEditor;

namespace EMT.Packages.Editor
{
    /// <summary>Persistent settings stored in EditorPrefs.</summary>
    public static class EMTPackageManagerSettings
    {
        private const string Prefix = "EMT.PackageManager.";
        private const string AutoCheckKey = Prefix + "AutoCheck";
        private const string IntervalKey = Prefix + "IntervalHours";
        private const string TokenKey = Prefix + "GitHubToken";

        public const int DefaultIntervalHours = 24;
        public static readonly int[] IntervalOptionsHours = { 6, 12, 24, 72, 168 };
        public static readonly string[] IntervalLabels = { "6 hours", "12 hours", "24 hours", "3 days", "7 days" };

        public static bool AutoCheck
        {
            get => EditorPrefs.GetBool(AutoCheckKey, true);
            set => EditorPrefs.SetBool(AutoCheckKey, value);
        }

        public static int CheckIntervalHours
        {
            get
            {
                int v = EditorPrefs.GetInt(IntervalKey, DefaultIntervalHours);
                return Array.IndexOf(IntervalOptionsHours, v) >= 0 ? v : DefaultIntervalHours;
            }
            set => EditorPrefs.SetInt(IntervalKey, value);
        }

        public static int CheckIntervalIndex => Math.Max(0, Array.IndexOf(IntervalOptionsHours, CheckIntervalHours));
        public static TimeSpan CheckInterval => TimeSpan.FromHours(CheckIntervalHours);

        /// <summary>Cached GitHub results expire after the configured check interval.</summary>
        public static TimeSpan CacheTtl => CheckInterval;

        /// <summary>
        /// Optional GitHub token (read-only) for private repositories / higher rate limits.
        /// NOTE: EditorPrefs stores values unencrypted on this machine.
        /// </summary>
        public static string GitHubToken
        {
            get => EditorPrefs.GetString(TokenKey, string.Empty);
            set => EditorPrefs.SetString(TokenKey, (value ?? string.Empty).Trim());
        }

        // Last check time is stored per project (productGUID) so projects do not throttle each other.
        private static string LastCheckKey => Prefix + "LastCheckUtcTicks." + PlayerSettings.productGUID.ToString("N");

        public static DateTime? LastCheckUtc
        {
            get
            {
                string s = EditorPrefs.GetString(LastCheckKey, string.Empty);
                if (long.TryParse(s, out long ticks) && ticks > 0 && ticks <= DateTime.MaxValue.Ticks)
                    return new DateTime(ticks, DateTimeKind.Utc);
                return null;
            }
        }

        public static void MarkChecked(DateTime utcNow) => EditorPrefs.SetString(LastCheckKey, utcNow.Ticks.ToString());

        /// <summary>True when automatic checking is on and the interval since the last check has elapsed.</summary>
        public static bool IsAutoCheckDue(DateTime utcNow)
        {
            if (!AutoCheck) return false;
            DateTime? last = LastCheckUtc;
            if (!last.HasValue) return true;
            TimeSpan elapsed = utcNow - last.Value;
            return elapsed < TimeSpan.Zero || elapsed >= CheckInterval; // negative = clock changed
        }
    }
}
