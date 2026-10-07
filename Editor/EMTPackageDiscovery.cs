using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace EMT.Packages.Editor
{
    /// <summary>
    /// Discovers installed com.emt.* packages through the Unity Package Manager API.
    /// There is intentionally no hardcoded package list.
    /// </summary>
    public static class EMTPackageDiscovery
    {
        public const string RequiredPrefix = "com.emt.";

        /// <summary>
        /// Strict check: the name must start with exactly "com.emt." (case-sensitive) and have something after it.
        /// </summary>
        public static bool IsEMTPackage(string packageName)
        {
            return !string.IsNullOrEmpty(packageName)
                   && packageName.Length > RequiredPrefix.Length
                   && packageName.StartsWith(RequiredPrefix, StringComparison.Ordinal);
        }

        /// <summary>Asynchronously lists installed packages (Client.List) and returns the com.emt.* ones.</summary>
        public static void DiscoverAsync(Action<List<EMTPackageInfo>> onSuccess, Action<string> onError)
        {
            ListRequest request;
            try
            {
                // offlineMode: the installed list does not need the network. Indirect deps included.
                request = Client.List(true, true);
            }
            catch (Exception e)
            {
                onError?.Invoke(e.Message);
                return;
            }

            EditorApplication.CallbackFunction poll = null;
            poll = () =>
            {
                if (!request.IsCompleted) return;
                EditorApplication.update -= poll;

                if (request.Status != StatusCode.Success)
                {
                    onError?.Invoke(request.Error != null ? request.Error.message : "Unknown Package Manager error.");
                    return;
                }

                List<EMTPackageInfo> result;
                try
                {
                    result = FromUnityPackages(request.Result);
                }
                catch (Exception e)
                {
                    onError?.Invoke(e.Message);
                    return;
                }

                onSuccess?.Invoke(result);
            };
            EditorApplication.update += poll;
        }

        /// <summary>Filters and converts Unity package infos into EMT models.</summary>
        public static List<EMTPackageInfo> FromUnityPackages(IEnumerable<UnityPackageInfo> packages)
        {
            var result = new List<EMTPackageInfo>();
            if (packages == null) return result;

            foreach (UnityPackageInfo p in packages)
            {
                if (p == null || !IsEMTPackage(p.name)) continue;
                result.Add(ToEMTInfo(p));
            }

            result.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        private static EMTPackageInfo ToEMTInfo(UnityPackageInfo p)
        {
            EMTPackageSource source = MapSource(p.source);

            string gitRef = null;
            string locationRepo = null;
            if (source == EMTPackageSource.Git)
            {
                string location = GetLocation(p.packageId, p.name);
                if (!string.IsNullOrEmpty(location))
                {
                    int hash = location.LastIndexOf('#');
                    locationRepo = hash >= 0 ? location.Substring(0, hash) : location;
                    gitRef = hash >= 0 ? location.Substring(hash + 1) : null;
                }
            }

            // package.json repository.url is the source of truth; the install URL is only a fallback.
            string repo = p.repository != null ? p.repository.url : null;
            if (string.IsNullOrWhiteSpace(repo) && !string.IsNullOrEmpty(locationRepo))
                repo = StripQuery(locationRepo);

            return new EMTPackageInfo
            {
                Name = p.name,
                DisplayName = string.IsNullOrWhiteSpace(p.displayName) ? p.name : p.displayName,
                InstalledVersion = p.version,
                AssetPath = p.assetPath,
                PackageId = p.packageId,
                Source = source,
                RepositoryUrl = string.IsNullOrWhiteSpace(repo) ? null : repo.Trim(),
                GitRef = gitRef
            };
        }

        private static EMTPackageSource MapSource(PackageSource source)
        {
            switch (source)
            {
                case PackageSource.Git: return EMTPackageSource.Git;
                case PackageSource.Local:
                case PackageSource.Embedded: return EMTPackageSource.Local;
                case PackageSource.Registry: return EMTPackageSource.Registry;
                default: return EMTPackageSource.Other;
            }
        }

        // packageId looks like "name@location" (e.g. com.emt.core@https://github.com/x/y.git#v1.2.0).
        private static string GetLocation(string packageId, string name)
        {
            if (string.IsNullOrEmpty(packageId)) return null;
            int at = packageId.IndexOf('@');
            return at >= 0 && at < packageId.Length - 1 ? packageId.Substring(at + 1) : null;
        }

        private static string StripQuery(string url)
        {
            int q = url.IndexOf('?');
            return q >= 0 ? url.Substring(0, q) : url;
        }
    }
}
