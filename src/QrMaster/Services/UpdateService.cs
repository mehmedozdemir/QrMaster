using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace QrMaster.Services;

public sealed record UpdateInfo(
    string Version,
    string Tag,
    UpdateChannel Channel,
    string ReleaseUrl,
    string Notes,
    string? AssetName,
    string? AssetUrl,
    long AssetSize,
    string? Sha256,
    string? Sha256Url);

/// <summary>Checks GitHub Releases for newer builds on the selected channel and installs them.</summary>
public sealed class UpdateService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("QrMaster", AppInfo.Version));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        c.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return c;
    }

    /// <summary>Maps a version to its channel: no label = Prod, "test" = Test, "dev" = Dev, anything else = ignored.</summary>
    internal static UpdateChannel? ChannelOf(SemVer v) => v.Label switch
    {
        null => UpdateChannel.Prod,
        "test" => UpdateChannel.Test,
        "dev" => UpdateChannel.Dev,
        _ => null
    };

    /// <summary>True when a release published on <paramref name="releaseChannel"/> should be offered to a user on <paramref name="userChannel"/>.</summary>
    internal static bool IsEligible(UpdateChannel releaseChannel, UpdateChannel userChannel) =>
        releaseChannel <= userChannel;

    private static string AssetSuffix => RuntimeInformation.OSArchitecture == Architecture.Arm64
        ? "-win-arm64.exe"
        : "-win-x64.exe";

    public async Task<UpdateInfo?> CheckAsync(UpdateChannel channel, CancellationToken ct = default)
    {
        if (!AppInfo.IsRepositoryConfigured) return null;
        if (!SemVer.TryParse(AppInfo.Version, out var current)) return null;

        var url = $"https://api.github.com/repos/{AppInfo.Repository}/releases?per_page=30";
        await using var stream = await Http.GetStreamAsync(url, ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

        return PickBest(doc.RootElement, current, channel);
    }

    internal static UpdateInfo? PickBest(JsonElement releases, SemVer current, UpdateChannel channel)
    {
        SemVer? bestVer = null;
        JsonElement best = default;
        UpdateChannel bestChannel = default;

        foreach (var r in releases.EnumerateArray())
        {
            if (r.TryGetProperty("draft", out var d) && d.GetBoolean()) continue;
            var tag = r.GetProperty("tag_name").GetString();
            if (!SemVer.TryParse(tag, out var v)) continue;
            if (ChannelOf(v) is not { } rc || !IsEligible(rc, channel)) continue;
            if (bestVer is null || v > bestVer)
            {
                bestVer = v; best = r; bestChannel = rc;
            }
        }

        if (bestVer is null || !(bestVer > current)) return null;

        string? name = null, assetUrl = null, digest = null, shaUrl = null;
        long size = 0;
        if (best.TryGetProperty("assets", out var assets))
        {
            foreach (var a in assets.EnumerateArray())
            {
                var n = a.GetProperty("name").GetString() ?? "";
                if (n.EndsWith(AssetSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    name = n;
                    assetUrl = a.GetProperty("browser_download_url").GetString();
                    size = a.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
                    if (a.TryGetProperty("digest", out var dg) && dg.ValueKind == JsonValueKind.String)
                    {
                        var s = dg.GetString();
                        if (s?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true) digest = s[7..];
                    }
                }
            }
            if (name is not null)
            {
                foreach (var a in assets.EnumerateArray())
                {
                    if (string.Equals(a.GetProperty("name").GetString(), name + ".sha256", StringComparison.OrdinalIgnoreCase))
                        shaUrl = a.GetProperty("browser_download_url").GetString();
                }
            }
        }

        return new UpdateInfo(
            bestVer.ToString(),
            best.GetProperty("tag_name").GetString() ?? "",
            bestChannel,
            best.GetProperty("html_url").GetString() ?? $"https://github.com/{AppInfo.Repository}/releases",
            best.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
            name, assetUrl, size, digest, shaUrl);
    }

    public bool CanInstall(UpdateInfo u) =>
        OperatingSystem.IsWindows() && u.AssetUrl is not null && (u.Sha256 is not null || u.Sha256Url is not null);

    /// <summary>Downloads the installer and verifies its SHA-256. Returns the local path.</summary>
    public async Task<string> DownloadAsync(UpdateInfo u, IProgress<double>? progress, CancellationToken ct)
    {
        if (u.AssetUrl is null || u.AssetName is null) throw new InvalidOperationException("No installer asset.");

        var expected = u.Sha256;
        if (expected is null && u.Sha256Url is not null)
        {
            var text = await Http.GetStringAsync(u.Sha256Url, ct).ConfigureAwait(false);
            expected = text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        }
        if (string.IsNullOrWhiteSpace(expected)) throw new InvalidOperationException("Missing checksum.");

        var dir = Path.Combine(Path.GetTempPath(), "QrMaster", "update");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Path.GetFileName(u.AssetName));

        using var resp = await Http.GetAsync(u.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? u.AssetSize;

        using var sha = SHA256.Create();
        await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var dst = File.Create(path))
        {
            var buf = new byte[81920];
            long read = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                sha.TransformBlock(buf, 0, n, null, 0);
                read += n;
                if (total > 0) progress?.Report((double)read / total);
            }
            sha.TransformFinalBlock([], 0, 0);
        }

        var actual = Convert.ToHexString(sha.Hash!);
        if (!string.Equals(actual, expected.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(path); } catch { }
            throw new InvalidDataException("Checksum mismatch.");
        }
        return path;
    }

    public static void LaunchInstaller(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            Arguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /LAUNCH=1",
            UseShellExecute = true
        });
    }
}
