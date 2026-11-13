using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace DownhillRoguelite.Core.Update;

public enum UpdateKind
{
    /// <summary>A small update made for this folder's full release (patches/update.pck and changed files).</summary>
    Patch,
    /// <summary>A whole new Windows build (a new full release).</summary>
    Full,
}

/// <summary>What the newest release offers this game folder.</summary>
public sealed record UpdateOffer(
    UpdateKind Kind,
    // The version the folder reports afterwards (version.txt): the update's version, or the release's tag.
    string Version,
    string Tag,
    string ReleaseName,
    string AssetName,
    string Url,
    long Size,
    // Lower-case hex SHA-256 of the download, from GitHub's asset digest; empty when GitHub gave none.
    string Sha256,
    string PageUrl)
{
    public double SizeMb => Size / 1048576.0;
}

/// <summary>
/// Decides from GitHub's release list (the JSON of GET /repos/{owner}/{repo}/releases) whether this
/// game folder is behind, and what to download. Shared word for word by the game (UpdateCheck), the
/// "Let's Play" launcher and, in PowerShell, export/playtest/update.ps1 — keep the three in step.
///
/// The rules, newest release first (drafts skipped; pre-releases only when asked):
/// - a release with a small update made for this folder's full release (base.txt) decides: up to date
///   if its version is the one installed (version.txt), otherwise that update;
/// - else a release with a Windows build decides: up to date if its tag is this folder's base or
///   installed version, otherwise the whole build (a build made on the team's PC, "*-windows.zip",
///   is preferred over the CI's "*-windows-full.zip", which lacks the licensed character art);
/// - a release with neither (only Linux, or nothing attached yet) is passed over.
/// </summary>
public static class UpdatePlanner
{
    public const string Repo = "vstimac/DownhillRoguelite";
    public const string ReleasesUrl = "https://api.github.com/repos/" + Repo + "/releases?per_page=20";
    public const string PatchPrefix = "DownhillRoguelite-update-";

    /// <summary>The update for a folder whose full release is <paramref name="baseTag"/> and which reports <paramref name="installed"/>, or null when it is up to date (or nothing fits).</summary>
    public static UpdateOffer? Plan(string releasesJson, string baseTag, string installed, bool includePrerelease = false)
    {
        baseTag = baseTag.Trim();
        installed = installed.Trim();
        if (installed.Length == 0)
        {
            installed = baseTag;
        }
        using JsonDocument document = JsonDocument.Parse(releasesJson);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("the release list is not a JSON array");
        }

        List<JsonElement> releases = new();
        foreach (JsonElement release in document.RootElement.EnumerateArray())
        {
            if (Bool(release, "draft") || (Bool(release, "prerelease") && !includePrerelease))
            {
                continue;
            }
            releases.Add(release);
        }
        // GitHub lists newest first by creation; publishing order is what players see.
        releases.Sort((a, b) => Published(b).CompareTo(Published(a)));

        foreach (JsonElement release in releases)
        {
            string tag = Text(release, "tag_name");
            string name = Text(release, "name");
            if (name.Length == 0)
            {
                name = tag;
            }
            string page = Text(release, "html_url");
            JsonElement? patch = null;
            string patchVersion = "";
            JsonElement? full = null;
            if (release.TryGetProperty("assets", out JsonElement assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement asset in assets.EnumerateArray())
                {
                    string assetName = Text(asset, "name");
                    string? version = PatchVersion(assetName, baseTag);
                    if (version != null && patch == null)
                    {
                        patch = asset;
                        patchVersion = version;
                    }
                    else if (IsFullWindowsBuild(assetName))
                    {
                        // The team's own build first; the CI's "-full" one only if there is no other.
                        if (full == null || (IsCiBuild(Text(full.Value, "name")) && !IsCiBuild(assetName)))
                        {
                            full = asset;
                        }
                    }
                }
            }
            if (patch != null)
            {
                if (string.Equals(patchVersion, installed, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                return Offer(UpdateKind.Patch, patchVersion, tag, name, page, patch.Value);
            }
            if (full != null)
            {
                if (string.Equals(tag, baseTag, StringComparison.OrdinalIgnoreCase) || string.Equals(tag, installed, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                return Offer(UpdateKind.Full, tag, tag, name, page, full.Value);
            }
        }
        return null;
    }

    /// <summary>The version in "DownhillRoguelite-update-&lt;version&gt;-for-&lt;base&gt;.zip" when it is made for <paramref name="baseTag"/>, else null.</summary>
    public static string? PatchVersion(string assetName, string baseTag)
    {
        if (baseTag.Length == 0 || !assetName.StartsWith(PatchPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        string suffix = "-for-" + baseTag + ".zip";
        if (!assetName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        string version = assetName.Substring(PatchPrefix.Length, assetName.Length - PatchPrefix.Length - suffix.Length);
        return version.Length > 0 ? version : null;
    }

    /// <summary>A whole Windows build: "...-windows.zip" or the CI's "...-windows-full.zip" (not an update).</summary>
    public static bool IsFullWindowsBuild(string assetName)
    {
        if (assetName.StartsWith(PatchPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return assetName.EndsWith("-windows.zip", StringComparison.OrdinalIgnoreCase)
            || assetName.EndsWith("-windows-full.zip", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCiBuild(string assetName) => assetName.EndsWith("-windows-full.zip", StringComparison.OrdinalIgnoreCase);

    private static UpdateOffer Offer(UpdateKind kind, string version, string tag, string name, string page, JsonElement asset)
    {
        string digest = Text(asset, "digest");
        string sha = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest.Substring(7).ToLowerInvariant() : "";
        long size = asset.TryGetProperty("size", out JsonElement s) && s.ValueKind == JsonValueKind.Number ? s.GetInt64() : 0;
        return new UpdateOffer(kind, version, tag, name, Text(asset, "name"), Text(asset, "browser_download_url"), size, sha, page);
    }

    private static string Text(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    }

    private static bool Bool(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.True;
    }

    private static DateTime Published(JsonElement release)
    {
        string when = Text(release, "published_at");
        if (when.Length == 0)
        {
            when = Text(release, "created_at");
        }
        return DateTime.TryParse(when, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed) ? parsed : DateTime.MinValue;
    }
}
