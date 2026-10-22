using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace BetterRichPresence.Discord
{
    public class DetectableExecutable
    {
        [SerializationPropertyName("name")]
        public string Name { get; set; }

        [SerializationPropertyName("os")]
        public string Os { get; set; }

        [SerializationPropertyName("is_launcher")]
        public bool IsLauncher { get; set; }
    }

    public class DetectableSku
    {
        [SerializationPropertyName("distributor")]
        public string Distributor { get; set; }

        [SerializationPropertyName("id")]
        public string Id { get; set; }
    }

    public class DetectableApp
    {
        [SerializationPropertyName("id")]
        public string Id { get; set; }

        [SerializationPropertyName("name")]
        public string Name { get; set; }

        [SerializationPropertyName("icon_hash")]
        public string Icon { get; set; }

        [SerializationPropertyName("aliases")]
        public List<string> Aliases { get; set; }

        [SerializationPropertyName("executables")]
        public List<DetectableExecutable> Executables { get; set; }

        [SerializationPropertyName("third_party_skus")]
        public List<DetectableSku> ThirdPartySkus { get; set; }

        public string IconUrl => string.IsNullOrEmpty(Icon) ? null : $"https://cdn.discordapp.com/app-icons/{Id}/{Icon}.png?size=512";
    }

    /// <summary>
    /// Discord's public list of games it can detect. Using a game's own application ID
    /// makes Discord show "Playing &lt;game&gt;" instead of "Playing Playnite".
    /// </summary>
    internal sealed class DetectableAppsCatalog
    {
        private const string Endpoint = "https://discord.com/api/v9/applications/detectable";
        private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
        private static readonly ILogger logger = LogManager.GetLogger();

        // Bare names shared by many games; matching on them would pick a random application.
        private static readonly HashSet<string> GenericExecutables = new HashSet<string>
        {
            "game.exe", "launcher.exe", "start.exe", "play.exe", "client.exe", "main.exe",
            "app.exe", "win64/game.exe", "bin/game.exe", "unitycrashhandler64.exe", "unitycrashhandler32.exe"
        };

        private readonly string cachePath;
        private Dictionary<string, DetectableApp> byName = new Dictionary<string, DetectableApp>();
        private Dictionary<string, DetectableApp> byId = new Dictionary<string, DetectableApp>();
        private Dictionary<string, DetectableApp> bySteamId = new Dictionary<string, DetectableApp>();
        private List<KeyValuePair<string, DetectableApp>> byExecutable = new List<KeyValuePair<string, DetectableApp>>();

        public int Count => byId.Count;

        public DetectableAppsCatalog(string dataPath)
        {
            cachePath = Path.Combine(dataPath, "detectable.json");
        }

        public async Task LoadAsync(bool forceRefresh = false)
        {
            try
            {
                var cacheFresh = File.Exists(cachePath) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath) < MaxAge;
                if (cacheFresh && !forceRefresh)
                {
                    Index(Serialization.FromJsonFile<List<DetectableApp>>(cachePath));
                    return;
                }

                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var http = new HttpClient())
                {
                    var json = await http.GetStringAsync(Endpoint).ConfigureAwait(false);
                    var apps = Serialization.FromJson<List<DetectableApp>>(json);
                    foreach (var app in apps)
                    {
                        // Only keep what we use; the raw list is several megabytes.
                        // Launcher executables would make a game show as its launcher.
                        app.Executables = app.Executables?.Where(e => e.Os == "win32" && !e.IsLauncher).ToList();
                        app.ThirdPartySkus = app.ThirdPartySkus?.Where(s => s.Distributor == "steam").ToList();
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
                    File.WriteAllText(cachePath, Serialization.ToJson(apps), Encoding.UTF8);
                    Index(apps);
                }
            }
            catch (Exception e)
            {
                logger.Error(e, "Failed to load Discord detectable applications");
                if (Count == 0 && File.Exists(cachePath))
                {
                    try
                    {
                        Index(Serialization.FromJsonFile<List<DetectableApp>>(cachePath));
                    }
                    catch (Exception cacheError)
                    {
                        logger.Error(cacheError, "Failed to read cached detectable applications");
                    }
                }
            }
        }

        public DetectableApp FindById(string id)
        {
            return id != null && byId.TryGetValue(id, out var app) ? app : null;
        }

        public DetectableApp Find(string gameName, string executablePath, string steamAppId = null)
        {
            if (steamAppId != null && bySteamId.TryGetValue(steamAppId, out var bySteam))
            {
                return bySteam;
            }

            // Name before executable: the started process is often a launcher (Steam, EA app…) that Discord also knows.
            var key = Normalize(gameName);
            if (key.Length > 0 && byName.TryGetValue(key, out var byGameName))
            {
                return byGameName;
            }

            if (!string.IsNullOrEmpty(executablePath))
            {
                var exe = executablePath.Replace('\\', '/').ToLowerInvariant();
                foreach (var entry in byExecutable)
                {
                    if (exe == entry.Key || exe.EndsWith("/" + entry.Key, StringComparison.Ordinal))
                    {
                        return entry.Value;
                    }
                }
            }

            return null;
        }

        private void Index(List<DetectableApp> apps)
        {
            var names = new Dictionary<string, DetectableApp>();
            var ids = new Dictionary<string, DetectableApp>();
            var steamIds = new Dictionary<string, DetectableApp>();
            var executables = new List<KeyValuePair<string, DetectableApp>>();

            foreach (var app in apps.Where(a => !string.IsNullOrEmpty(a.Id)))
            {
                ids[app.Id] = app;
                foreach (var sku in app.ThirdPartySkus ?? Enumerable.Empty<DetectableSku>())
                {
                    if (sku.Distributor == "steam" && !string.IsNullOrEmpty(sku.Id) && !steamIds.ContainsKey(sku.Id))
                    {
                        steamIds[sku.Id] = app;
                    }
                }

                foreach (var name in new[] { app.Name }.Concat(app.Aliases ?? Enumerable.Empty<string>()))
                {
                    var key = Normalize(name);
                    if (key.Length > 0 && !names.ContainsKey(key))
                    {
                        names[key] = app;
                    }
                }

                foreach (var exe in app.Executables ?? Enumerable.Empty<DetectableExecutable>())
                {
                    // Discord prefixes some entries with ">" to mean "exact path".
                    var name = exe.Name?.TrimStart('>').Replace('\\', '/').ToLowerInvariant();
                    if (!string.IsNullOrEmpty(name) && !GenericExecutables.Contains(name))
                    {
                        executables.Add(new KeyValuePair<string, DetectableApp>(name, app));
                    }
                }
            }

            // Longest paths first so "game/bin/game.exe" beats a bare "game.exe" from another app.
            executables.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));

            byName = names;
            byId = ids;
            bySteamId = steamIds;
            byExecutable = executables;
        }

        internal static string Normalize(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(name.Length);
            foreach (var c in name.Replace("&", "and").ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }
    }
}
