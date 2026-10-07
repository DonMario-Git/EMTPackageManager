using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace EMT.Packages.Editor
{
    public enum EMTRegistryState { NotChecked, Installed, Available, Unavailable, Invalid }

    /// <summary>One package listed in the default registry file.</summary>
    public sealed class EMTRegistryEntry
    {
        public string Name;
        public string Repository;
        public EMTRegistryState State = EMTRegistryState.NotChecked;
        public string InstalledVersion;
        public string LatestTag;
        public string LatestVersion;
        public string Message;
    }

    /// <summary>
    /// Default list of packages the user wants available, stored in ProjectSettings/EMTPackageRegistry.json.
    /// It is user data, not code: installed packages are still discovered through Client.List().
    /// </summary>
    public static class EMTPackageRegistry
    {
        [Serializable] private sealed class Item { public string name; public string repository; }
        [Serializable] private sealed class RegistryFile { public List<Item> packages = new List<Item>(); }

        public const string Template = @"{
  ""packages"": [
    {
      ""name"": ""com.emt.core"",
      ""repository"": ""https://github.com/DonMario-Git/EMTCore.git""
    }
  ]
}
";

        public static string FilePath =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ProjectSettings", "EMTPackageRegistry.json");

        public static bool Exists => File.Exists(FilePath);

        public static List<EMTRegistryEntry> Load(out string error)
        {
            error = null;
            if (!Exists) return new List<EMTRegistryEntry>();
            try { return Parse(File.ReadAllText(FilePath), out error); }
            catch (Exception e)
            {
                error = "Could not read registry file: " + e.Message;
                return new List<EMTRegistryEntry>();
            }
        }

        /// <summary>Parses and validates registry JSON. Never throws.</summary>
        public static List<EMTRegistryEntry> Parse(string json, out string error)
        {
            error = null;
            var list = new List<EMTRegistryEntry>();

            RegistryFile data;
            try { data = JsonUtility.FromJson<RegistryFile>(json); }
            catch (Exception e)
            {
                error = "Invalid registry JSON: " + e.Message;
                return list;
            }

            if (data == null || data.packages == null)
            {
                error = "Registry JSON has no 'packages' array.";
                return list;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Item item in data.packages)
            {
                if (item == null) continue;
                var entry = new EMTRegistryEntry
                {
                    Name = item.name != null ? item.name.Trim() : null,
                    Repository = item.repository != null ? item.repository.Trim() : null
                };

                if (!EMTPackageDiscovery.IsEMTPackage(entry.Name))
                    Invalidate(entry, "Name must start with '" + EMTPackageDiscovery.RequiredPrefix + "'.");
                else if (!seen.Add(entry.Name))
                    Invalidate(entry, "Duplicate entry.");
                else if (!EMTGitHubClient.TryParseRepository(entry.Repository, out _, out _))
                    Invalidate(entry, "Repository is not a valid GitHub URL.");

                list.Add(entry);
            }
            return list;
        }

        public static bool CreateTemplate(out string error)
        {
            error = null;
            try
            {
                if (!Exists) File.WriteAllText(FilePath, Template, new UTF8Encoding(false));
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        private static void Invalidate(EMTRegistryEntry e, string message)
        {
            e.State = EMTRegistryState.Invalid;
            e.Message = message;
        }
    }
}
