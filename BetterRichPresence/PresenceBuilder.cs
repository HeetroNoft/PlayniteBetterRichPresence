using BetterRichPresence.Discord;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BetterRichPresence
{
    internal sealed class RunningGame
    {
        public Game Game { get; set; }
        public DateTime StartedUtc { get; set; }
        public string ExecutablePath { get; set; }
        /// <summary>The game's own process, 0 until found.</summary>
        public int ProcessId { get; set; }
    }

    internal sealed class PresenceBuilder
    {
        private static readonly Guid SteamPluginId = Guid.Parse("cb91dfc9-b977-43bf-8e70-55f46e410fab");
        private static readonly Regex Placeholder = new Regex(@"\{(\w+)\}", RegexOptions.Compiled);
        // A separator left dangling at either end, or doubled, once a placeholder rendered empty.
        private static readonly Regex EdgeSeparator = new Regex(@"^\s*[·•|\-–]\s*|\s*[·•|\-–]\s*$", RegexOptions.Compiled);
        private static readonly Regex DoubleSeparator = new Regex(@"(\s[·•|\-–]\s)(\s*[·•|\-–]\s*)+", RegexOptions.Compiled);

        private readonly BetterRichPresenceSettings settings;
        private readonly DetectableAppsCatalog catalog;
        private readonly Func<string, string> playniteImageUrl;
        private readonly Func<Guid, string> libraryName;

        /// <param name="playniteImageUrl">Maps a Playnite image (icon or cover field) to a public URL, or null if it has none yet.</param>
        /// <param name="libraryName">Name of the library plugin with that ID, or null if none is installed.</param>
        public PresenceBuilder(BetterRichPresenceSettings settings, DetectableAppsCatalog catalog,
            Func<string, string> playniteImageUrl, Func<Guid, string> libraryName)
        {
            this.settings = settings;
            this.catalog = catalog;
            this.playniteImageUrl = playniteImageUrl;
            this.libraryName = libraryName;
        }

        public Presence ForGame(RunningGame running)
        {
            var game = running.Game;
            var app = ResolveApplication(running);
            var matched = app != null;

            var values = GameValues(game);
            var activity = new Dictionary<string, object> { ["type"] = 0 };
            AddText(activity, "details", Render(matched ? settings.MatchedDetails : settings.FallbackDetails, values));
            AddText(activity, "state", Render(matched ? settings.MatchedState : settings.FallbackState, values));
            activity["status_display_type"] = (int)(matched ? settings.MatchedStatusDisplay : settings.FallbackStatusDisplay);

            if (settings.ShowElapsedTime)
            {
                activity["timestamps"] = new Dictionary<string, object> { ["start"] = ToUnixSeconds(running.StartedUtc) };
            }

            // The fallback image belongs to the fallback application; an asset key wouldn't resolve under a game's own application.
            var fallbackImage = matched || string.IsNullOrEmpty(settings.FallbackImage) ? null : settings.FallbackImage;
            var assets = new Dictionary<string, object>();
            var image = settings.ShowGameImage ? GameImage(game, app) : null;
            if (image == null && settings.ShowPlayniteBadge)
            {
                // Discord only draws the small image over a large one: give the badge the icon
                // Discord would show for the game anyway.
                image = app?.IconUrl;
            }

            if (image != null)
            {
                assets["large_image"] = image;
                assets["large_text"] = Truncate(game.Name, 128);
                if (settings.ShowPlayniteBadge)
                {
                    // Asset keys only resolve inside their own application, so games Discord
                    // knows (their own application) get the logo by URL instead.
                    assets["small_image"] = fallbackImage ?? BetterRichPresenceSettings.PlayniteLogoUrl;
                    assets["small_text"] = "via Playnite";
                }
            }
            else if (fallbackImage != null)
            {
                assets["large_image"] = fallbackImage;
                assets["large_text"] = "Playnite";
            }

            if (assets.Count > 0)
            {
                activity["assets"] = assets;
            }

            return new Presence
            {
                ApplicationId = app?.Id ?? settings.FallbackApplicationId,
                ProcessId = running.ProcessId,
                Activity = activity
            };
        }

        public Presence Idle(ICollection<Game> library)
        {
            var playtime = library.Aggregate(0UL, (total, g) => total + g.Playtime);
            var games = library.Count.ToString();
            var values = new Dictionary<string, string>
            {
                ["games"] = games,
                ["installed"] = library.Count(g => g.IsInstalled).ToString(),
                ["favorites"] = library.Count(g => g.Favorite).ToString(),
                ["played"] = library.Count(g => g.Playtime > 0).ToString(),
                ["playtime"] = playtime > 0 ? FormatDuration(playtime) : null,
                // Name used before 0.1.0 shipped; kept so existing templates keep working.
                ["gamecount"] = games
            };
            var activity = new Dictionary<string, object> { ["type"] = 0 };
            AddText(activity, "details", Render(settings.IdleDetails, values));
            AddText(activity, "state", Render(settings.IdleState, values));
            if (!string.IsNullOrEmpty(settings.FallbackImage))
            {
                activity["assets"] = new Dictionary<string, object> { ["large_image"] = settings.FallbackImage, ["large_text"] = "Playnite" };
            }

            return new Presence { ApplicationId = settings.FallbackApplicationId, Activity = activity };
        }

        private DetectableApp ResolveApplication(RunningGame running)
        {
            if (settings.ApplicationOverrides.TryGetValue(running.Game.Id, out var overrideId))
            {
                // A user-chosen ID may not be in the catalog; still honour it.
                return catalog.FindById(overrideId) ?? new DetectableApp { Id = overrideId, Name = running.Game.Name };
            }

            return settings.UseGameApplications
                ? catalog.Find(running.Game.Name, running.ExecutablePath, SteamAppId(running.Game))
                : null;
        }

        internal static string SteamAppId(Game game)
        {
            return game.PluginId == SteamPluginId && !string.IsNullOrEmpty(game.GameId) ? game.GameId : null;
        }

        private Dictionary<string, string> GameValues(Game game)
        {
            return new Dictionary<string, string>
            {
                ["name"] = game.Name,
                ["platform"] = game.Platforms?.FirstOrDefault()?.Name,
                ["source"] = game.Source?.Name,
                // Games added by hand (or whose library plugin is gone) belong to Playnite itself.
                ["library"] = (game.PluginId == Guid.Empty ? null : libraryName(game.PluginId)) ?? "Playnite",
                ["playtime"] = game.Playtime > 0 ? FormatDuration(game.Playtime) : null,
                ["playcount"] = game.PlayCount > 0 ? game.PlayCount.ToString() : null,
                ["completion"] = game.CompletionStatus?.Name,
                ["genre"] = game.Genres?.FirstOrDefault()?.Name,
                ["developer"] = game.Developers?.FirstOrDefault()?.Name,
                ["year"] = game.ReleaseYear?.ToString(),
                ["score"] = game.UserScore?.ToString()
            };
        }

        internal static string Render(string template, Dictionary<string, string> values)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                return null;
            }

            var text = Placeholder.Replace(template, m =>
                values.TryGetValue(m.Groups[1].Value.ToLowerInvariant(), out var value) ? value ?? string.Empty : m.Value);
            text = DoubleSeparator.Replace(text, "$1");
            text = EdgeSeparator.Replace(text, string.Empty).Trim();

            // A template made only of placeholders that are all empty (e.g. "{playtime} played" on a new game)
            // would leave a meaningless fragment; drop lines that lost every placeholder value.
            var hadPlaceholders = Placeholder.IsMatch(template);
            var anyValue = Placeholder.Matches(template).Cast<Match>()
                .Any(m => values.TryGetValue(m.Groups[1].Value.ToLowerInvariant(), out var v) && !string.IsNullOrEmpty(v));
            return hadPlaceholders && !anyValue ? null : text;
        }

        private static void AddText(Dictionary<string, object> activity, string key, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            // Discord rejects strings shorter than 2 characters.
            activity[key] = Truncate(text.Length < 2 ? text + " " : text, 128);
        }

        internal static string ChosenImage(Game game, GameImageKind kind)
        {
            return kind == GameImageKind.Icon ? game.Icon : game.CoverImage;
        }

        private string GameImage(Game game, DetectableApp app)
        {
            // The image the user picked in Playnite wins whenever Discord can reach it.
            var chosen = ChosenImage(game, settings.GameImage);
            var chosenUrl = string.IsNullOrEmpty(chosen) ? null : playniteImageUrl(chosen);
            if (chosenUrl != null)
            {
                return chosenUrl;
            }

            var steamAppId = SteamAppId(game);
            if (steamAppId != null)
            {
                return $"https://cdn.cloudflare.steamstatic.com/steam/apps/{steamAppId}/library_600x900.jpg";
            }

            return app?.IconUrl;
        }

        internal static bool IsHttpUrl(string value)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        private static string FormatDuration(ulong seconds)
        {
            var hours = seconds / 3600;
            var minutes = seconds % 3600 / 60;
            return hours > 0 ? $"{hours}h {minutes:00}m" : $"{minutes}m";
        }

        private static long ToUnixSeconds(DateTime utc)
        {
            return (long)(utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        private static string Truncate(string text, int max)
        {
            return text.Length <= max ? text : text.Substring(0, max - 1) + "…";
        }
    }
}
