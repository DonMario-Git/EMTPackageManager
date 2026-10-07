using System;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace EMT.Packages.Editor
{
    public enum EMTGitHubError
    {
        None,
        InvalidRepository,
        NoConnection,
        Timeout,
        RateLimited,
        NotFoundOrPrivate,
        Unauthorized,
        ServerError,
        InvalidResponse,
        InvalidVersion,
        Unknown
    }

    public sealed class EMTReleaseResult
    {
        public bool Success { get; private set; }
        public EMTGitHubError Error { get; private set; }
        public string Message { get; private set; }
        public string TagName { get; private set; }
        public string ReleaseUrl { get; private set; }

        public static EMTReleaseResult Ok(string tag, string url) =>
            new EMTReleaseResult { Success = true, TagName = tag, ReleaseUrl = url };

        public static EMTReleaseResult Fail(EMTGitHubError error, string message) =>
            new EMTReleaseResult { Success = false, Error = error, Message = message };
    }

    /// <summary>
    /// Queries GitHub Releases metadata only (never downloads or executes anything).
    /// All requests are asynchronous: the Editor is never blocked.
    /// </summary>
    public static class EMTGitHubClient
    {
        private const string ApiRoot = "https://api.github.com";
        private const int TimeoutSeconds = 15;
        private const string GitHubWebPrefix = "https://github.com/";

        private static readonly Regex NamePattern = new Regex(@"^[A-Za-z0-9_.\-]+$", RegexOptions.CultureInvariant);

        [Serializable]
        private sealed class ReleaseDto
        {
            public string tag_name;
            public string html_url;
            public bool draft;
            public bool prerelease;
        }

        /// <summary>
        /// Extracts owner/repo from https, ssh or scp-style GitHub URLs
        /// (".git" suffix, "?path=" query and "#ref" fragment are ignored).
        /// </summary>
        public static bool TryParseRepository(string url, out string owner, out string repo)
        {
            owner = null;
            repo = null;
            if (string.IsNullOrWhiteSpace(url)) return false;

            string s = url.Trim();
            int cut = s.IndexOfAny(new[] { '?', '#' });
            if (cut >= 0) s = s.Substring(0, cut);
            if (s.StartsWith("git+", StringComparison.OrdinalIgnoreCase)) s = s.Substring(4);

            string path;
            const string scp = "git@github.com:";
            if (s.StartsWith(scp, StringComparison.OrdinalIgnoreCase))
            {
                path = s.Substring(scp.Length);
            }
            else if (Uri.TryCreate(s, UriKind.Absolute, out Uri uri)
                     && (uri.Scheme == "https" || uri.Scheme == "http" || uri.Scheme == "ssh" || uri.Scheme == "git")
                     && (string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(uri.Host, "www.github.com", StringComparison.OrdinalIgnoreCase)))
            {
                path = uri.AbsolutePath;
            }
            else
            {
                return false;
            }

            path = path.Trim('/');
            if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                path = path.Substring(0, path.Length - 4);

            string[] parts = path.Split('/');
            if (parts.Length != 2) return false;
            foreach (string part in parts)
            {
                if (part.Length == 0 || part == "." || part == ".." || !NamePattern.IsMatch(part))
                    return false;
            }

            owner = parts[0];
            repo = parts[1];
            return true;
        }

        public static string BuildLatestReleaseUrl(string owner, string repo) =>
            ApiRoot + "/repos/" + Uri.EscapeDataString(owner) + "/" + Uri.EscapeDataString(repo) + "/releases/latest";

        /// <summary>Asynchronously fetches the latest published (non-draft, non-prerelease) release.</summary>
        public static void GetLatestReleaseAsync(string repositoryUrl, string token, Action<EMTReleaseResult> callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            if (!TryParseRepository(repositoryUrl, out string owner, out string repo))
            {
                callback(EMTReleaseResult.Fail(EMTGitHubError.InvalidRepository,
                    "'" + repositoryUrl + "' is not a valid GitHub repository URL."));
                return;
            }

            UnityWebRequest request = null;
            try
            {
                request = UnityWebRequest.Get(BuildLatestReleaseUrl(owner, repo));
                request.timeout = TimeoutSeconds;
                request.SetRequestHeader("Accept", "application/vnd.github+json");
                request.SetRequestHeader("User-Agent", "EMT-PackageManager");
                request.SetRequestHeader("X-GitHub-Api-Version", "2022-11-28");
                if (IsSafeToken(token))
                    request.SetRequestHeader("Authorization", "Bearer " + token.Trim());
                request.SendWebRequest();
            }
            catch (Exception e)
            {
                if (request != null) request.Dispose();
                callback(EMTReleaseResult.Fail(EMTGitHubError.Unknown, e.Message));
                return;
            }

            EditorApplication.CallbackFunction poll = null;
            poll = () =>
            {
                if (!request.isDone) return;
                EditorApplication.update -= poll;

                EMTReleaseResult result;
                try
                {
                    result = Classify(request);
                }
                catch (Exception e)
                {
                    result = EMTReleaseResult.Fail(EMTGitHubError.Unknown, e.Message);
                }
                finally
                {
                    request.Dispose();
                }

                callback(result);
            };
            EditorApplication.update += poll;
        }

        /// <summary>Parses the JSON of GET /releases/latest. Never throws.</summary>
        public static EMTReleaseResult ParseLatestRelease(string json)
        {
            ReleaseDto dto;
            try
            {
                dto = JsonUtility.FromJson<ReleaseDto>(json);
            }
            catch (Exception)
            {
                return EMTReleaseResult.Fail(EMTGitHubError.InvalidResponse, "GitHub returned invalid JSON.");
            }

            if (dto == null || string.IsNullOrWhiteSpace(dto.tag_name))
                return EMTReleaseResult.Fail(EMTGitHubError.InvalidResponse, "GitHub response has no release tag.");

            if (dto.draft)
                return EMTReleaseResult.Fail(EMTGitHubError.NotFoundOrPrivate, "The latest release is a draft.");

            if (!EMTVersionUtility.TryParse(dto.tag_name, out _))
                return EMTReleaseResult.Fail(EMTGitHubError.InvalidVersion,
                    "Release tag '" + dto.tag_name + "' is not a valid SemVer version.");

            // Only keep links that point to github.com (they may be opened in the browser later).
            string url = !string.IsNullOrEmpty(dto.html_url) && dto.html_url.StartsWith(GitHubWebPrefix, StringComparison.Ordinal)
                ? dto.html_url
                : null;

            return EMTReleaseResult.Ok(dto.tag_name.Trim(), url);
        }

        private static EMTReleaseResult Classify(UnityWebRequest request)
        {
            switch (request.result)
            {
                case UnityWebRequest.Result.Success:
                    return ParseLatestRelease(request.downloadHandler != null ? request.downloadHandler.text : null);

                case UnityWebRequest.Result.ConnectionError:
                {
                    string err = request.error ?? string.Empty;
                    if (err.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        err.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0)
                        return EMTReleaseResult.Fail(EMTGitHubError.Timeout, "GitHub did not respond in time.");
                    return EMTReleaseResult.Fail(EMTGitHubError.NoConnection,
                        "Could not reach GitHub. Check your internet connection.");
                }

                case UnityWebRequest.Result.ProtocolError:
                    return ClassifyHttpError(request);

                case UnityWebRequest.Result.DataProcessingError:
                    return EMTReleaseResult.Fail(EMTGitHubError.InvalidResponse, "Could not process GitHub's response.");

                default:
                    return EMTReleaseResult.Fail(EMTGitHubError.Unknown, request.error ?? "Unknown error.");
            }
        }

        private static EMTReleaseResult ClassifyHttpError(UnityWebRequest request)
        {
            long code = request.responseCode;

            if (code == 401)
                return EMTReleaseResult.Fail(EMTGitHubError.Unauthorized, "GitHub rejected the token (401). Check it in Settings.");

            if (code == 403 || code == 429)
            {
                string remaining = request.GetResponseHeader("X-RateLimit-Remaining");
                string body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                bool limited = code == 429 || remaining == "0" ||
                               (body != null && body.IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0);
                if (limited)
                {
                    string when = string.Empty;
                    if (long.TryParse(request.GetResponseHeader("X-RateLimit-Reset"), out long reset))
                    {
                        try { when = " Resets at " + DateTimeOffset.FromUnixTimeSeconds(reset).LocalDateTime.ToString("t") + "."; }
                        catch (Exception) { /* ignore malformed header */ }
                    }
                    return EMTReleaseResult.Fail(EMTGitHubError.RateLimited,
                        "GitHub API rate limit reached." + when + " A token in Settings raises the limit.");
                }

                return EMTReleaseResult.Fail(EMTGitHubError.Unauthorized,
                    "Access denied (403). The repository may be private or the token lacks permission.");
            }

            if (code == 404)
                return EMTReleaseResult.Fail(EMTGitHubError.NotFoundOrPrivate,
                    "No published release found, or the repository does not exist / is private " +
                    "(add a GitHub token in Settings for private repositories).");

            if (code >= 500)
                return EMTReleaseResult.Fail(EMTGitHubError.ServerError, "GitHub is unavailable (HTTP " + code + ").");

            return EMTReleaseResult.Fail(EMTGitHubError.Unknown, "Unexpected HTTP status " + code + ".");
        }

        private static bool IsSafeToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            foreach (char c in token)
                if (char.IsControl(c)) return false; // avoid header injection
            return true;
        }
    }
}
