namespace EMT.Packages.Editor
{
    /// <summary>Turns "installed vs latest" into an <see cref="EMTUpdateStatus"/>.</summary>
    public static class EMTPackageVersionChecker
    {
        public static EMTUpdateStatus Evaluate(string installedVersion, string latestVersion)
        {
            if (!EMTVersionUtility.TryParse(installedVersion, out EMTSemVer installed) ||
                !EMTVersionUtility.TryParse(latestVersion, out EMTSemVer latest))
                return EMTUpdateStatus.InvalidVersion;

            int cmp = EMTVersionUtility.Compare(installed, latest);

            // Installed == Latest or Installed > Latest (e.g. a pre-release / dev build ahead of the last release).
            if (cmp >= 0) return EMTUpdateStatus.UpToDate;

            return latest.Major > installed.Major
                ? EMTUpdateStatus.MajorUpdateAvailable
                : EMTUpdateStatus.UpdateAvailable;
        }
    }
}
