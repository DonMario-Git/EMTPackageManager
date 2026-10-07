using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace EMT.Packages.Editor
{
    public sealed class EMTUpdateResult
    {
        public bool Success;
        public string Message;
        public List<string> UpdatedPackages = new List<string>();
        public List<string> SkippedPackages = new List<string>(); // "name: reason"
    }

    /// <summary>
    /// Updates a package by editing ONLY its value in Packages/manifest.json (targeted text replacement),
    /// then asks Unity Package Manager to resolve. Nothing is downloaded or executed here.
    /// </summary>
    public static class EMTPackageUpdater
    {
        private static readonly Regex SafeRef = new Regex(@"^[A-Za-z0-9][A-Za-z0-9._+\-]*$", RegexOptions.CultureInvariant);

        private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        public static string ManifestPath => Path.Combine(ProjectRoot, "Packages", "manifest.json");

        public static bool CanUpdate(EMTPackageInfo package, out string reason)
        {
            reason = null;
            if (package == null) { reason = "No package."; return false; }
            if (package.Source != EMTPackageSource.Git)
            {
                reason = "Only packages installed from Git can be updated.";
                return false;
            }
            if (!package.HasUpdate) { reason = "No update available."; return false; }
            if (string.IsNullOrEmpty(package.LatestTag) || !EMTVersionUtility.TryParse(package.LatestTag, out _))
            {
                reason = "The latest release was not validated.";
                return false;
            }
            return true;
        }

        /// <summary>Applies all valid updates with a single manifest write and a single Resolve.</summary>
        public static EMTUpdateResult Update(IEnumerable<EMTPackageInfo> packages)
        {
            var result = new EMTUpdateResult();

            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return Fail(result, "Exit Play Mode before updating packages.");

            string path = ManifestPath;
            string original;
            try
            {
                if (!File.Exists(path)) return Fail(result, "Packages/manifest.json was not found.");
                original = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                return Fail(result, "Could not read manifest.json: " + e.Message);
            }

            string current = original;
            if (packages != null)
            {
                foreach (EMTPackageInfo p in packages)
                {
                    if (!CanUpdate(p, out string reason))
                    {
                        result.SkippedPackages.Add((p != null ? p.Name : "?") + ": " + reason);
                        continue;
                    }

                    if (TryReplaceDependency(current, p.Name, p.LatestTag, p.RepositoryUrl, out string next, out string error))
                    {
                        current = next;
                        result.UpdatedPackages.Add(p.Name);
                    }
                    else
                    {
                        result.SkippedPackages.Add(p.Name + ": " + error);
                    }
                }
            }

            if (result.UpdatedPackages.Count == 0)
                return Fail(result, "Nothing was updated. manifest.json was not modified." + Describe(result.SkippedPackages));

            try
            {
                Directory.CreateDirectory(EMTUpdateCache.CacheDirectory);
                File.WriteAllText(Path.Combine(EMTUpdateCache.CacheDirectory, "manifest.backup.json"), original, new UTF8Encoding(false));

                string tmp = Path.Combine(EMTUpdateCache.CacheDirectory, "manifest.new.json");
                File.WriteAllText(tmp, current, new UTF8Encoding(false));
                File.Copy(tmp, path, true);
                File.Delete(tmp);
            }
            catch (Exception e)
            {
                return Fail(result, "Could not write manifest.json: " + e.Message);
            }

            Client.Resolve(); // let Unity Package Manager re-resolve the changed references

            result.Success = true;
            result.Message = "Updated " + result.UpdatedPackages.Count + " package(s): " +
                             string.Join(", ", result.UpdatedPackages) +
                             ". Unity is resolving packages (a backup of the previous manifest is in Library/EMTPackageManager)." +
                             Describe(result.SkippedPackages);
            return result;
        }

        /// <summary>
        /// Pure function: replaces only the "#ref" of the Git dependency <paramref name="packageName"/>
        /// and leaves every other character of the manifest untouched.
        /// </summary>
        public static bool TryReplaceDependency(
            string manifestJson, string packageName, string newTag, string expectedRepositoryUrl,
            out string updatedJson, out string error)
        {
            updatedJson = manifestJson;
            error = null;

            if (string.IsNullOrEmpty(manifestJson)) { error = "manifest.json is empty."; return false; }
            if (!EMTPackageDiscovery.IsEMTPackage(packageName)) { error = "Not a com.emt.* package."; return false; }
            if (string.IsNullOrEmpty(newTag) || !SafeRef.IsMatch(newTag) || !EMTVersionUtility.TryParse(newTag, out _))
            {
                error = "Release tag '" + newTag + "' is not a valid SemVer tag.";
                return false;
            }

            var regex = new Regex("(\"" + Regex.Escape(packageName) + "\"\\s*:\\s*\")((?:[^\"\\\\]|\\\\.)*)(\")",
                RegexOptions.CultureInvariant);
            MatchCollection matches = regex.Matches(manifestJson);

            if (matches.Count == 0)
            {
                error = "'" + packageName + "' is not declared in manifest.json (it may be an indirect dependency).";
                return false;
            }
            if (matches.Count > 1) { error = "'" + packageName + "' appears more than once in manifest.json."; return false; }

            Group valueGroup = matches[0].Groups[2];
            string current = valueGroup.Value;

            if (current.IndexOf('\\') >= 0) { error = "The manifest entry contains escape sequences; edit it manually."; return false; }
            if (!IsGitDependency(current))
            {
                error = "'" + packageName + "' is not a Git dependency in manifest.json ('" + current + "').";
                return false;
            }

            int hash = current.LastIndexOf('#');
            string baseUrl = hash >= 0 ? current.Substring(0, hash) : current; // keeps ?path=... if present

            if (!string.IsNullOrEmpty(expectedRepositoryUrl) &&
                EMTGitHubClient.TryParseRepository(expectedRepositoryUrl, out string eo, out string er))
            {
                if (!EMTGitHubClient.TryParseRepository(baseUrl, out string mo, out string mr) ||
                    !string.Equals(eo, mo, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(er, mr, StringComparison.OrdinalIgnoreCase))
                {
                    error = "The manifest URL points to a different repository than package.json (" + eo + "/" + er + ").";
                    return false;
                }
            }

            string replacement = baseUrl + "#" + newTag;
            if (string.Equals(replacement, current, StringComparison.Ordinal))
            {
                error = "manifest.json already references " + newTag + ".";
                return false;
            }

            updatedJson = manifestJson.Substring(0, valueGroup.Index) + replacement +
                          manifestJson.Substring(valueGroup.Index + valueGroup.Length);
            return true;
        }

        public static bool IsGitDependency(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (value.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return false;
            if (EMTVersionUtility.TryParse(value, out _)) return false; // registry version
            return value.StartsWith("git@", StringComparison.OrdinalIgnoreCase)
                   || value.StartsWith("git+", StringComparison.OrdinalIgnoreCase)
                   || value.IndexOf("://", StringComparison.Ordinal) > 0;
        }

        private static EMTUpdateResult Fail(EMTUpdateResult r, string message)
        {
            r.Success = false;
            r.Message = message;
            return r;
        }

        private static string Describe(List<string> skipped) =>
            skipped.Count == 0 ? string.Empty : "\nSkipped:\n- " + string.Join("\n- ", skipped);
    }
}
