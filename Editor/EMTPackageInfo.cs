using System;

namespace EMT.Packages.Editor
{
    /// <summary>Where a package was installed from.</summary>
    public enum EMTPackageSource
    {
        Git,
        Local,      // file: dependency or embedded in /Packages
        Registry,
        Other       // built-in, tarball, unknown
    }

    public enum EMTUpdateStatus
    {
        NotChecked,
        UpToDate,
        UpdateAvailable,
        MajorUpdateAvailable,
        NoRepository,
        LocalDevelopment,
        CheckFailed,
        InvalidVersion
    }

    /// <summary>Model describing one com.emt.* package installed in the project.</summary>
    public sealed class EMTPackageInfo
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string InstalledVersion { get; set; }
        public string AssetPath { get; set; }
        public string PackageId { get; set; }
        public EMTPackageSource Source { get; set; }

        /// <summary>repository.url from package.json (source of truth).</summary>
        public string RepositoryUrl { get; set; }

        /// <summary>Git ref (tag/branch/hash) the project currently points to, if known.</summary>
        public string GitRef { get; set; }

        public string LatestVersion { get; set; }
        public string LatestTag { get; set; }
        public string ReleaseUrl { get; set; }

        public EMTUpdateStatus Status { get; set; } = EMTUpdateStatus.NotChecked;
        public string StatusMessage { get; set; }
        public DateTime LastCheckedUtc { get; set; }

        public bool IsLocal => Source == EMTPackageSource.Local;

        public bool SupportsUpdateCheck =>
            Source == EMTPackageSource.Git && !string.IsNullOrWhiteSpace(RepositoryUrl);

        public bool HasUpdate =>
            Status == EMTUpdateStatus.UpdateAvailable || Status == EMTUpdateStatus.MajorUpdateAvailable;
    }
}
