using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace EMT.Packages.Editor
{
    /// <summary>UI Toolkit window: EMT > Package Manager.</summary>
    public sealed class EMTPackageManagerWindow : EditorWindow
    {
        private Button _checkButton;
        private Button _updateAllButton;
        private Label _statusLabel;
        private Label _lastCheckLabel;
        private ScrollView _list;
        private VisualElement _registryList;

        [MenuItem("EMT/Package Manager")]
        public static void Open()
        {
            var window = GetWindow<EMTPackageManagerWindow>();
            window.titleContent = new GUIContent("EMT Package Manager");
            window.minSize = new Vector2(480, 340);
            window.Show();
        }

        private void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.Clear();
            SetPadding(root, 8);

            // Header
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 6;

            var title = new Label("EMT PACKAGE MANAGER");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 15;
            title.style.flexGrow = 1;
            header.Add(title);

            _checkButton = new Button(OnCheckClicked) { text = "Check for Updates" };
            _updateAllButton = new Button(OnUpdateAllClicked) { text = "Update All" };
            header.Add(_checkButton);
            header.Add(_updateAllButton);
            root.Add(header);

            // Settings
            var foldout = new Foldout { text = "Settings", value = false };

            var auto = new Toggle("Check automatically") { value = EMTPackageManagerSettings.AutoCheck };
            auto.RegisterValueChangedCallback(e => EMTPackageManagerSettings.AutoCheck = e.newValue);
            foldout.Add(auto);

            var interval = new DropdownField("Check interval",
                new List<string>(EMTPackageManagerSettings.IntervalLabels),
                EMTPackageManagerSettings.CheckIntervalIndex);
            interval.RegisterValueChangedCallback(_ =>
            {
                int i = interval.index;
                if (i >= 0 && i < EMTPackageManagerSettings.IntervalOptionsHours.Length)
                    EMTPackageManagerSettings.CheckIntervalHours = EMTPackageManagerSettings.IntervalOptionsHours[i];
            });
            foldout.Add(interval);

            var token = new TextField("GitHub token (optional)")
            {
                value = EMTPackageManagerSettings.GitHubToken,
                isPasswordField = true
            };
            token.RegisterValueChangedCallback(e => EMTPackageManagerSettings.GitHubToken = e.newValue);
            foldout.Add(token);

            var tokenHint = new Label("Only needed for private repositories or higher rate limits. Use a read-only token. " +
                                      "It is stored unencrypted in this machine's EditorPrefs.");
            tokenHint.style.whiteSpace = WhiteSpace.Normal;
            tokenHint.style.fontSize = 10;
            tokenHint.style.color = new Color(0.6f, 0.6f, 0.6f);
            foldout.Add(tokenHint);

            // Default package registry (JSON) + availability
            var regTitle = new Label("Package registry (ProjectSettings/EMTPackageRegistry.json)");
            regTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            regTitle.style.marginTop = 8;
            foldout.Add(regTitle);

            var regButtons = new VisualElement();
            regButtons.style.flexDirection = FlexDirection.Row;
            regButtons.Add(new Button(OnCreateRegistry) { text = "Create File" });
            regButtons.Add(new Button(() => { if (EMTPackageRegistry.Exists) EditorUtility.OpenWithDefaultApp(EMTPackageRegistry.FilePath); }) { text = "Open File" });
            regButtons.Add(new Button(() => EMTPackageManager.CheckRegistry(true)) { text = "Reload / Check" });
            foldout.Add(regButtons);

            _registryList = new VisualElement();
            foldout.Add(_registryList);
            root.Add(foldout);

            // Status
            _statusLabel = new Label();
            _statusLabel.style.marginTop = 4;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            root.Add(_statusLabel);

            _lastCheckLabel = new Label();
            _lastCheckLabel.style.fontSize = 10;
            _lastCheckLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            _lastCheckLabel.style.marginBottom = 6;
            root.Add(_lastCheckLabel);

            // List
            _list = new ScrollView();
            _list.style.flexGrow = 1;
            root.Add(_list);

            EMTPackageManager.Changed -= OnManagerChanged;
            EMTPackageManager.Changed += OnManagerChanged;

            if (!EMTPackageManager.HasLoaded && !EMTPackageManager.IsChecking)
                EMTPackageManager.Refresh(false, EMTPackageManagerSettings.AutoCheck); // cache first; network only if auto-check is on

            Rebuild();
        }

        private void OnDisable() => EMTPackageManager.Changed -= OnManagerChanged;

        private void OnManagerChanged()
        {
            if (this == null || _list == null) return;
            Rebuild();
        }

        // ---------------------------------------------------------------- actions

        private static void OnCheckClicked() => EMTPackageManager.Refresh(true);

        private void OnUpdateAllClicked()
        {
            var normal = EMTPackageManager.Packages.Where(p => p.Status == EMTUpdateStatus.UpdateAvailable).ToList();
            var majors = EMTPackageManager.Packages.Where(p => p.Status == EMTUpdateStatus.MajorUpdateAvailable).ToList();

            if (normal.Count == 0 && majors.Count == 0)
            {
                EditorUtility.DisplayDialog("Update All", "There is nothing to update.", "OK");
                return;
            }

            var toUpdate = new List<EMTPackageInfo>();

            if (normal.Count > 0)
            {
                string lines = string.Join("\n", normal.Select(p => "• " + p.DisplayName + ": " + p.InstalledVersion + " → " + p.LatestVersion));
                if (!EditorUtility.DisplayDialog("Update All",
                        "These packages will be updated:\n\n" + lines + "\n\nAre you sure you want to update?",
                        "Update", "Cancel"))
                    return;
                toUpdate.AddRange(normal);
            }

            if (majors.Count > 0)
            {
                string lines = string.Join("\n", majors.Select(p => "• " + p.DisplayName + ": " + p.InstalledVersion + " → " + p.LatestVersion));
                // 0 = ok, 1 = cancel, 2 = alt
                int choice = EditorUtility.DisplayDialogComplex("WARNING - Major updates",
                    "These updates contain breaking changes:\n\n" + lines + "\n\nReview the migration guides before updating.",
                    "Update Anyway", "Cancel", "Skip Major Updates");
                if (choice == 1) return;
                if (choice == 0) toUpdate.AddRange(majors);
            }

            if (toUpdate.Count > 0) RunUpdate(toUpdate);
        }

        private static void ConfirmAndUpdate(EMTPackageInfo p)
        {
            bool major = p.Status == EMTUpdateStatus.MajorUpdateAvailable;
            bool confirmed;

            if (major)
            {
                confirmed = EditorUtility.DisplayDialog("WARNING",
                    "This update contains breaking changes.\n\n" +
                    p.DisplayName + "\n" + p.InstalledVersion + " → " + p.LatestVersion +
                    "\n\nReview the migration guide before updating.",
                    "Update Anyway", "Cancel");
            }
            else
            {
                confirmed = EditorUtility.DisplayDialog("Update " + p.DisplayName,
                    p.DisplayName + "\n\nInstalled:\n" + p.InstalledVersion +
                    "\n\nAvailable:\n" + p.LatestVersion +
                    "\n\nRepository:\nGitHub\n\nAre you sure you want to update?",
                    "Update", "Cancel");
            }

            if (confirmed) RunUpdate(new List<EMTPackageInfo> { p });
        }

        private static void RunUpdate(List<EMTPackageInfo> packages)
        {
            EMTUpdateResult result = EMTPackageManager.UpdatePackages(packages);
            if (!result.Success) EditorUtility.DisplayDialog("Update failed", result.Message, "OK");
            else Debug.Log("[EMT Package Manager] " + result.Message);
        }

        // ---------------------------------------------------------------- rendering

        private void Rebuild()
        {
            IReadOnlyList<EMTPackageInfo> packages = EMTPackageManager.Packages;
            bool checking = EMTPackageManager.IsChecking;

            _checkButton.SetEnabled(!checking);
            _updateAllButton.SetEnabled(!checking && packages.Any(p => p.HasUpdate));
            _statusLabel.text = BuildStatusText(packages);

            System.DateTime? last = EMTPackageManagerSettings.LastCheckUtc;
            _lastCheckLabel.text = "Last check: " + (last.HasValue ? last.Value.ToLocalTime().ToString("g") : "never");

            RebuildRegistry();

            _list.Clear();
            if (packages.Count == 0)
            {
                var empty = new Label(EMTPackageManager.HasLoaded
                    ? "No com.emt.* packages are installed in this project."
                    : "Loading packages...");
                empty.style.marginTop = 10;
                _list.Add(empty);
                return;
            }

            foreach (EMTPackageInfo p in packages) _list.Add(BuildRow(p));
        }

        private static string BuildStatusText(IReadOnlyList<EMTPackageInfo> packages)
        {
            if (EMTPackageManager.IsChecking) return "Checking for updates...";
            if (!string.IsNullOrEmpty(EMTPackageManager.LastErrorMessage))
                return "Could not check for updates. " + EMTPackageManager.LastErrorMessage;

            int failed = packages.Count(p => p.Status == EMTUpdateStatus.CheckFailed);
            if (failed > 0) return "Could not check for updates (" + failed + " package(s)). See the details below.";

            int updates = packages.Count(p => p.HasUpdate);
            if (updates > 0) return updates + " update(s) available.";

            return packages.Count > 0 ? "No updates found." : string.Empty;
        }

        private static VisualElement BuildRow(EMTPackageInfo p)
        {
            Color accent = StatusColor(p.Status);

            var card = new VisualElement();
            card.style.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.12f);
            card.style.marginBottom = 6;
            SetPadding(card, 8);
            SetRadius(card, 4);
            card.style.borderLeftWidth = 3;
            card.style.borderLeftColor = accent;

            // Title + badge
            var top = new VisualElement();
            top.style.flexDirection = FlexDirection.Row;
            top.style.alignItems = Align.Center;

            var names = new VisualElement();
            names.style.flexGrow = 1;
            var display = new Label(p.DisplayName);
            display.style.unityFontStyleAndWeight = FontStyle.Bold;
            display.style.fontSize = 13;
            names.Add(display);
            names.Add(Small(p.Name));
            top.Add(names);

            var badge = new Label(StatusText(p.Status));
            badge.style.backgroundColor = accent;
            badge.style.color = Color.white;
            badge.style.unityFontStyleAndWeight = FontStyle.Bold;
            badge.style.paddingLeft = 8;
            badge.style.paddingRight = 8;
            badge.style.paddingTop = 2;
            badge.style.paddingBottom = 2;
            SetRadius(badge, 8);
            top.Add(badge);
            card.Add(top);

            // Details
            var details = new VisualElement();
            details.style.marginTop = 4;
            details.Add(new Label("Installed: " + (p.InstalledVersion ?? "?")));
            if (p.Source == EMTPackageSource.Local)
                details.Add(new Label("Local Development"));
            else
                details.Add(new Label("Latest:    " + (string.IsNullOrEmpty(p.LatestVersion) ? "—" : p.LatestVersion)));
            details.Add(Small("Source: " + p.Source + (string.IsNullOrEmpty(p.RepositoryUrl) ? string.Empty : "  •  " + p.RepositoryUrl)));
            card.Add(details);

            if (!string.IsNullOrEmpty(p.StatusMessage))
            {
                Label msg = Small(p.StatusMessage);
                msg.style.whiteSpace = WhiteSpace.Normal;
                msg.style.marginTop = 2;
                card.Add(msg);
            }

            // Actions
            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.justifyContent = Justify.FlexEnd;
            actions.style.marginTop = 4;

            if (!string.IsNullOrEmpty(p.ReleaseUrl) && p.ReleaseUrl.StartsWith("https://github.com/"))
            {
                string url = p.ReleaseUrl;
                actions.Add(new Button(() => Application.OpenURL(url)) { text = "Release Notes" });
            }

            if (p.HasUpdate)
            {
                EMTPackageInfo captured = p;
                var update = new Button(() => ConfirmAndUpdate(captured))
                {
                    text = p.Status == EMTUpdateStatus.MajorUpdateAvailable ? "Update (Major)" : "Update"
                };
                update.SetEnabled(!EMTPackageManager.IsChecking);
                actions.Add(update);
            }

            if (actions.childCount > 0) card.Add(actions);
            return card;
        }

        private static void OnCreateRegistry()
        {
            if (!EMTPackageRegistry.CreateTemplate(out string error))
                EditorUtility.DisplayDialog("Registry", "Could not create the file: " + error, "OK");
            else
                EMTPackageManager.CheckRegistry(false);
        }

        private void RebuildRegistry()
        {
            if (_registryList == null) return;
            _registryList.Clear();

            if (!EMTPackageRegistry.Exists)
            {
                _registryList.Add(Small("No registry file yet. Create one to list the packages you want available."));
                return;
            }

            if (!string.IsNullOrEmpty(EMTPackageManager.RegistryError))
                _registryList.Add(Small(EMTPackageManager.RegistryError));
            if (EMTPackageManager.IsCheckingRegistry)
                _registryList.Add(Small("Checking availability..."));
            if (EMTPackageManager.Registry.Count == 0 && string.IsNullOrEmpty(EMTPackageManager.RegistryError))
                _registryList.Add(Small("The registry is empty."));

            foreach (EMTRegistryEntry e in EMTPackageManager.Registry)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginTop = 2;

                var name = new Label(e.Name ?? "(no name)");
                name.style.flexGrow = 1;
                row.Add(name);

                var state = new Label(RegistryText(e));
                state.style.color = RegistryColor(e.State);
                state.style.unityFontStyleAndWeight = FontStyle.Bold;
                row.Add(state);

                if (e.State == EMTRegistryState.Available)
                {
                    EMTRegistryEntry captured = e;
                    row.Add(new Button(() => ConfirmAndInstall(captured)) { text = "Install" });
                }
                _registryList.Add(row);

                if (!string.IsNullOrEmpty(e.Message))
                {
                    Label msg = Small(e.Message);
                    msg.style.whiteSpace = WhiteSpace.Normal;
                    _registryList.Add(msg);
                }
            }
        }

        private static void ConfirmAndInstall(EMTRegistryEntry e)
        {
            if (!EditorUtility.DisplayDialog("Install " + e.Name,
                    e.Name + "\n\nVersion: " + e.LatestVersion + "\nRepository:\n" + e.Repository +
                    "\n\nThe package will be added to Packages/manifest.json. Continue?",
                    "Install", "Cancel"))
                return;

            string error = EMTPackageManager.InstallFromRegistry(e);
            if (error != null) EditorUtility.DisplayDialog("Install failed", error, "OK");
        }

        private static string RegistryText(EMTRegistryEntry e)
        {
            switch (e.State)
            {
                case EMTRegistryState.Installed: return "Installed " + e.InstalledVersion;
                case EMTRegistryState.Available: return "Available " + e.LatestVersion;
                case EMTRegistryState.Unavailable: return "Unavailable";
                case EMTRegistryState.Invalid: return "Invalid";
                default: return "Not checked";
            }
        }

        private static Color RegistryColor(EMTRegistryState s)
        {
            switch (s)
            {
                case EMTRegistryState.Installed: return new Color(0.30f, 0.75f, 0.40f);
                case EMTRegistryState.Available: return new Color(0.35f, 0.60f, 0.95f);
                case EMTRegistryState.Unavailable: return new Color(0.90f, 0.40f, 0.40f);
                case EMTRegistryState.Invalid: return new Color(0.75f, 0.45f, 0.80f);
                default: return new Color(0.6f, 0.6f, 0.6f);
            }
        }

        private static string StatusText(EMTUpdateStatus s)
        {
            switch (s)
            {
                case EMTUpdateStatus.UpToDate: return "Up to Date";
                case EMTUpdateStatus.UpdateAvailable: return "Update Available";
                case EMTUpdateStatus.MajorUpdateAvailable: return "Major Update Available";
                case EMTUpdateStatus.NoRepository: return "No Repository";
                case EMTUpdateStatus.LocalDevelopment: return "Local Development";
                case EMTUpdateStatus.CheckFailed: return "Check Failed";
                case EMTUpdateStatus.InvalidVersion: return "Invalid Version";
                default: return "Not Checked";
            }
        }

        private static Color StatusColor(EMTUpdateStatus s)
        {
            switch (s)
            {
                case EMTUpdateStatus.UpToDate: return new Color(0.18f, 0.55f, 0.28f);
                case EMTUpdateStatus.UpdateAvailable: return new Color(0.85f, 0.55f, 0.10f);
                case EMTUpdateStatus.MajorUpdateAvailable: return new Color(0.78f, 0.22f, 0.22f);
                case EMTUpdateStatus.LocalDevelopment: return new Color(0.25f, 0.45f, 0.75f);
                case EMTUpdateStatus.CheckFailed: return new Color(0.55f, 0.20f, 0.20f);
                case EMTUpdateStatus.InvalidVersion: return new Color(0.60f, 0.25f, 0.55f);
                default: return new Color(0.45f, 0.45f, 0.45f);
            }
        }

        private static Label Small(string text)
        {
            var l = new Label(text);
            l.style.fontSize = 10;
            l.style.color = new Color(0.6f, 0.6f, 0.6f);
            return l;
        }

        private static void SetPadding(VisualElement e, float v)
        {
            e.style.paddingLeft = v;
            e.style.paddingRight = v;
            e.style.paddingTop = v;
            e.style.paddingBottom = v;
        }

        private static void SetRadius(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = r;
            e.style.borderTopRightRadius = r;
            e.style.borderBottomLeftRadius = r;
            e.style.borderBottomRightRadius = r;
        }
    }
}
