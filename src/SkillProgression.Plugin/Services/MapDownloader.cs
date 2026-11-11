using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SkillProgression.Plugin.Services;

/// <summary>
/// Downloads a missing map from BeatSaver and registers it with SongCore (TECH_FINDINGS "Self-contained mode" §4,
/// same approach as BetterSongSearch). Must be awaited from the main thread (SongCore refresh).
/// </summary>
internal sealed class MapDownloader
{
    private readonly HttpClient _http;

    public MapDownloader(HttpClient http) => _http = http;

    public static bool IsDownloaded(string hash) => SongCore.Loader.GetLevelByHash(hash) != null;

    public async Task DownloadAsync(string hash, CancellationToken ct = default)
    {
        if (IsDownloaded(hash)) return;
        var lower = hash.ToLowerInvariant();

        // Metadata gives the folder name; the zip itself comes from the CDN.
        var meta = JObject.Parse(await _http.GetStringAsync($"https://api.beatsaver.com/maps/hash/{lower}"));
        var key = meta["id"]?.Value<string>() ?? lower.Substring(0, 8);
        var name = meta["metadata"]?["songName"]?.Value<string>() ?? "";
        var mapper = meta["metadata"]?["levelAuthorName"]?.Value<string>() ?? "";
        var url = meta["versions"]?.FirstOrDefault(v => string.Equals(v["hash"]?.Value<string>(), lower, StringComparison.OrdinalIgnoreCase))?["downloadURL"]?.Value<string>()
                  ?? $"https://r2cdn.beatsaver.com/{lower}.zip";

        var bytes = await Task.Run(async () =>
        {
            using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }, ct);

        var folder = Path.Combine(CustomLevelPathHelper.customLevelsDirectoryPath, Sanitize($"{key} ({name} - {mapper})"));
        await Task.Run(() => Extract(bytes, folder), ct);

        // Register with SongCore and wait until the level is loaded.
        SongCore.Loader.Instance.RefreshSongs(false);
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!IsDownloaded(hash))
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The map was downloaded but SongCore did not load it in time.");
            await Task.Delay(250, ct);
        }
    }

    private static void Extract(byte[] zip, string folder)
    {
        Directory.CreateDirectory(folder);
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            // Flat map zips only; refuse paths and oversized entries.
            if (entry.FullName.Contains('/') || entry.FullName.Contains('\\') || entry.Length > 200_000_000) continue;
            entry.ExtractToFile(Path.Combine(folder, entry.Name), overwrite: true);
        }
        if (!File.Exists(Path.Combine(folder, "Info.dat")) && !File.Exists(Path.Combine(folder, "info.dat")))
            throw new InvalidDataException("Downloaded map has no Info.dat.");
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Length > 80 ? name.Substring(0, 80) : name;
    }
}
