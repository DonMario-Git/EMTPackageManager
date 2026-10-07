using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace EMT.Packages.Editor
{
    [Serializable]
    public sealed class EMTCacheEntry
    {
        public string PackageName;
        public string Repository;
        public string InstalledVersion;
        public string LatestVersion;
        public string LatestTag;
        public string ReleaseUrl;
        public string LastCheckUtc; // ISO 8601 ("o")
        public string Status;
    }

    /// <summary>
    /// JSON cache in &lt;Project&gt;/Library/EMTPackageManager/update-cache.json (never committed to VCS).
    /// Only successful checks are cached. Entries expire after the configured interval.
    /// </summary>
    public static class EMTUpdateCache
    {
        [Serializable]
        private sealed class Store
        {
            public List<EMTCacheEntry> Entries = new List<EMTCacheEntry>();
        }

        private static Store _store;

        public static string CacheDirectory =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "EMTPackageManager");

        public static string FilePath => Path.Combine(CacheDirectory, "update-cache.json");

        public static bool TryGet(EMTPackageInfo package, TimeSpan ttl, DateTime nowUtc, out EMTCacheEntry entry)
        {
            entry = null;
            if (package == null || string.IsNullOrEmpty(package.RepositoryUrl)) return false;

            foreach (EMTCacheEntry e in Load().Entries)
            {
                if (e.PackageName != package.Name) continue;
                if (!string.Equals(e.Repository, package.RepositoryUrl, StringComparison.OrdinalIgnoreCase)) return false;
                if (string.IsNullOrEmpty(e.LatestTag)) return false;
                if (!DateTime.TryParse(e.LastCheckUtc, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime checkedAt))
                    return false;

                TimeSpan age = nowUtc - checkedAt;
                if (age < TimeSpan.FromMinutes(-5) || age >= ttl) return false; // clock skew or expired

                entry = e;
                return true;
            }
            return false;
        }

        public static void Put(EMTPackageInfo package)
        {
            if (package == null || string.IsNullOrEmpty(package.LatestTag)) return;
            if (package.Status != EMTUpdateStatus.UpToDate &&
                package.Status != EMTUpdateStatus.UpdateAvailable &&
                package.Status != EMTUpdateStatus.MajorUpdateAvailable) return; // never cache failures

            Store store = Load();
            store.Entries.RemoveAll(e => e.PackageName == package.Name);
            store.Entries.Add(new EMTCacheEntry
            {
                PackageName = package.Name,
                Repository = package.RepositoryUrl,
                InstalledVersion = package.InstalledVersion,
                LatestVersion = package.LatestVersion,
                LatestTag = package.LatestTag,
                ReleaseUrl = package.ReleaseUrl,
                LastCheckUtc = package.LastCheckedUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                Status = package.Status.ToString()
            });
            Save(store);
        }

        public static void Remove(string packageName)
        {
            Store store = Load();
            if (store.Entries.RemoveAll(e => e.PackageName == packageName) > 0) Save(store);
        }

        public static void Clear()
        {
            _store = new Store();
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch (Exception) { /* non-critical */ }
        }

        private static Store Load()
        {
            if (_store != null) return _store;
            _store = new Store();
            try
            {
                if (File.Exists(FilePath))
                {
                    Store loaded = JsonUtility.FromJson<Store>(File.ReadAllText(FilePath));
                    if (loaded != null && loaded.Entries != null) _store = loaded;
                }
            }
            catch (Exception)
            {
                _store = new Store(); // corrupt cache is simply ignored
            }
            return _store;
        }

        private static void Save(Store store)
        {
            try
            {
                Directory.CreateDirectory(CacheDirectory);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(store, true), new UTF8Encoding(false));
                File.Copy(tmp, FilePath, true);
                File.Delete(tmp);
            }
            catch (Exception)
            {
                // The cache is an optimisation only; failures must never break the Editor.
            }
        }
    }
}
