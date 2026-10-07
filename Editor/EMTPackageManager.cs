using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace EMT.Packages.Editor
{
    /// <summary>
    /// Orchestrator: discovery -> (cache | GitHub) -> version comparison -> state for the UI.
    /// Everything is asynchronous and non-blocking.
    /// </summary>
    [InitializeOnLoad]
    public static class EMTPackageManager
    {
        private const string StartupSessionKey = "EMT.PackageManager.StartupCheckDone";
        private const int MaxConcurrentRequests = 4;

        private static List<EMTPackageInfo> _packages = new List<EMTPackageInfo>();
        private static int _refreshId;

        public static event Action Changed;

        public static IReadOnlyList<EMTPackageInfo> Packages => _packages;
        public static bool IsChecking { get; private set; }
        public static bool HasLoaded { get; private set; }
        public static string LastErrorMessage { get; private set; }

        static EMTPackageManager()
        {
            // Packages installed/removed later (e.g. com.emt.localization) appear automatically.
            UnityEditor.PackageManager.Events.registeredPackages += OnRegisteredPackages;
            EditorApplication.delayCall += TryStartupCheck;
        }

        /// <summary>
        /// Re-discovers packages and refreshes their update state.
        /// forceNetwork ignores the cache ("Check for Updates"). allowNetwork=false only uses fresh cache entries.
        /// </summary>
        public static void Refresh(bool forceNetwork, bool allowNetwork = true, Action<bool> completed = null)
        {
            allowNetwork |= forceNetwork;
            int id = ++_refreshId;
            IsChecking = true;
            LastErrorMessage = null;
            Notify();

            EMTPackageDiscovery.DiscoverAsync(
                packages =>
                {
                    if (id != _refreshId) return;
                    OnDiscovered(packages, forceNetwork, allowNetwork, id, completed);
                },
                error =>
                {
                    if (id != _refreshId) return;
                    IsChecking = false;
                    LastErrorMessage = "Could not read installed packages: " + error;
                    Notify();
                    completed?.Invoke(false);
                });
        }

        public static EMTUpdateResult UpdatePackages(IEnumerable<EMTPackageInfo> packages)
        {
            EMTUpdateResult result = EMTPackageUpdater.Update(packages);
            if (result.Success)
            {
                foreach (string name in result.UpdatedPackages) EMTUpdateCache.Remove(name);
                foreach (EMTPackageInfo p in _packages)
                {
                    if (result.UpdatedPackages.Contains(p.Name))
                        p.StatusMessage = "Updated. Waiting for Unity Package Manager to resolve...";
                }
            }
            Notify();
            return result;
        }

        private static void OnDiscovered(List<EMTPackageInfo> packages, bool force, bool allowNetwork, int id, Action<bool> completed)
        {
            _packages = packages;
            HasLoaded = true;

            DateTime now = DateTime.UtcNow;
            TimeSpan ttl = EMTPackageManagerSettings.CacheTtl;

            // One request per unique repository (several packages may live in the same repo).
            var groups = new Dictionary<string, List<EMTPackageInfo>>(StringComparer.OrdinalIgnoreCase);

            foreach (EMTPackageInfo p in packages)
            {
                if (!NeedsRemoteCheck(p)) continue;

                if (!force && EMTUpdateCache.TryGet(p, ttl, now, out EMTCacheEntry cached))
                {
                    ApplyRelease(p, cached.LatestTag, cached.ReleaseUrl, ParseUtc(cached.LastCheckUtc, now));
                    continue;
                }

                if (!allowNetwork) continue; // stays NotChecked

                if (!EMTGitHubClient.TryParseRepository(p.RepositoryUrl, out string owner, out string repo))
                {
                    p.Status = EMTUpdateStatus.CheckFailed;
                    p.StatusMessage = "'" + p.RepositoryUrl + "' is not a valid GitHub repository URL.";
                    continue;
                }

                string key = (owner + "/" + repo).ToLowerInvariant();
                if (!groups.TryGetValue(key, out List<EMTPackageInfo> list))
                {
                    list = new List<EMTPackageInfo>();
                    groups.Add(key, list);
                }
                list.Add(p);
            }

            Notify();

            var queue = new Queue<List<EMTPackageInfo>>(groups.Values);
            if (queue.Count == 0)
            {
                Finish(false, false, completed);
                return;
            }

            int active = 0;
            int remaining = queue.Count;
            bool anySuccess = false;
            string token = EMTPackageManagerSettings.GitHubToken;

            void StartNext()
            {
                while (active < MaxConcurrentRequests && queue.Count > 0)
                {
                    List<EMTPackageInfo> group = queue.Dequeue();
                    active++;

                    EMTGitHubClient.GetLatestReleaseAsync(group[0].RepositoryUrl, token, result =>
                    {
                        if (id != _refreshId) return; // a newer refresh replaced this one

                        active--;
                        remaining--;
                        if (result.Success) anySuccess = true;
                        foreach (EMTPackageInfo pkg in group) ApplyResult(pkg, result);
                        Notify();

                        if (remaining == 0) Finish(true, anySuccess, completed);
                        else StartNext();
                    });
                }
            }

            StartNext();
        }

        /// <summary>Sets the static status and returns true if GitHub must be queried.</summary>
        private static bool NeedsRemoteCheck(EMTPackageInfo p)
        {
            p.LatestVersion = null;
            p.LatestTag = null;
            p.ReleaseUrl = null;
            p.StatusMessage = null;

            switch (p.Source)
            {
                case EMTPackageSource.Local:
                    p.Status = EMTUpdateStatus.LocalDevelopment;
                    p.StatusMessage = "Update check is not available for local development packages.";
                    return false;
                case EMTPackageSource.Git:
                    break;
                default:
                    p.Status = EMTUpdateStatus.NoRepository;
                    p.StatusMessage = "Not installed from Git; GitHub update checks are not available.";
                    return false;
            }

            if (string.IsNullOrWhiteSpace(p.RepositoryUrl))
            {
                p.Status = EMTUpdateStatus.NoRepository;
                p.StatusMessage = "package.json has no repository.url.";
                return false;
            }

            if (!EMTVersionUtility.TryParse(p.InstalledVersion, out _))
            {
                p.Status = EMTUpdateStatus.InvalidVersion;
                p.StatusMessage = "Installed version '" + p.InstalledVersion + "' is not valid SemVer.";
                return false;
            }

            p.Status = EMTUpdateStatus.NotChecked;
            return true;
        }

        private static void ApplyResult(EMTPackageInfo p, EMTReleaseResult r)
        {
            DateTime now = DateTime.UtcNow;
            p.LastCheckedUtc = now;

            if (!r.Success)
            {
                p.Status = r.Error == EMTGitHubError.InvalidVersion ? EMTUpdateStatus.InvalidVersion : EMTUpdateStatus.CheckFailed;
                p.StatusMessage = r.Message;
                return;
            }

            ApplyRelease(p, r.TagName, r.ReleaseUrl, now);
            EMTUpdateCache.Put(p);
        }

        private static void ApplyRelease(EMTPackageInfo p, string tag, string releaseUrl, DateTime checkedUtc)
        {
            p.LastCheckedUtc = checkedUtc;
            p.LatestTag = tag;
            p.ReleaseUrl = releaseUrl;
            p.LatestVersion = EMTVersionUtility.TryParse(tag, out EMTSemVer v) ? v.ToString() : tag;
            p.Status = EMTPackageVersionChecker.Evaluate(p.InstalledVersion, p.LatestVersion);
            p.StatusMessage = p.Status == EMTUpdateStatus.InvalidVersion
                ? "Release tag '" + tag + "' is not valid SemVer."
                : null;
        }

        private static void Finish(bool queried, bool anySuccess, Action<bool> completed)
        {
            IsChecking = false;
            if (queried && anySuccess) EMTPackageManagerSettings.MarkChecked(DateTime.UtcNow);
            Notify();
            completed?.Invoke(true);
        }

        private static DateTime ParseUtc(string s, DateTime fallback) =>
            DateTime.TryParse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind | DateTimeStyles.AdjustToUniversal, out DateTime d) ? d : fallback;

        private static void Notify()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        private static void OnRegisteredPackages(UnityEditor.PackageManager.PackageRegistrationEventArgs _)
        {
            if (!HasLoaded) return;
            EditorApplication.delayCall += () => Refresh(false, EMTPackageManagerSettings.AutoCheck);
        }

        // Runs once per Editor session, and only if the configured interval has elapsed.
        private static void TryStartupCheck()
        {
            if (Application.isBatchMode) return;
            if (SessionState.GetBool(StartupSessionKey, false)) return;
            SessionState.SetBool(StartupSessionKey, true);

            if (!EMTPackageManagerSettings.IsAutoCheckDue(DateTime.UtcNow)) return;

            Refresh(true, true, ok =>
            {
                if (!ok) return;
                int updates = 0;
                foreach (EMTPackageInfo p in _packages) if (p.HasUpdate) updates++;
                if (updates > 0)
                    Debug.Log("[EMT Package Manager] " + updates + " package update(s) available. Open EMT > Package Manager to review.");
            });
        }
    }
}
