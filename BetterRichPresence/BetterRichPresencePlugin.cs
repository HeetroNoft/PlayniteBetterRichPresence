using BetterRichPresence.Discord;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace BetterRichPresence
{
    public class BetterRichPresencePlugin : GenericPlugin
    {
        private const string MenuSection = "Better Rich Presence";
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly BetterRichPresenceSettingsViewModel settingsViewModel;
        private readonly PresenceManager presence = new PresenceManager();
        // Most recently started game last; several games can run at once.
        private readonly List<RunningGame> runningGames = new List<RunningGame>();

        private readonly ImageHost imageHost;
        // With "close to tray", closing the window keeps Playnite running: don't advertise browsing then.
        private volatile bool mainWindowShown = true;

        internal DetectableAppsCatalog Catalog { get; }
        private BetterRichPresenceSettings Settings => settingsViewModel.Settings;

        public override Guid Id { get; } = Guid.Parse("48a8ac35-6a9b-467e-ab79-153ed2fb6cab");

        public BetterRichPresencePlugin(IPlayniteAPI api) : base(api)
        {
            Catalog = new DetectableAppsCatalog(GetPluginUserDataPath());
            imageHost = new ImageHost(GetPluginUserDataPath());
            settingsViewModel = new BetterRichPresenceSettingsViewModel(this);
            Properties = new GenericPluginProperties { HasSettings = true };
        }

        public override async void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            WarnIfBuiltInPresenceEnabled();
            WatchMainWindow();
            RefreshPresence();
            await Catalog.LoadAsync();
            RefreshPresence();
        }

        private void WatchMainWindow()
        {
            var window = Application.Current?.MainWindow;
            if (window == null)
            {
                return;
            }

            void Update()
            {
                var shown = window.IsVisible && window.WindowState != WindowState.Minimized;
                if (shown != mainWindowShown)
                {
                    mainWindowShown = shown;
                    RefreshPresence();
                }
            }

            window.IsVisibleChanged += (s, e) => Update();
            window.StateChanged += (s, e) => Update();
            Update();
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            presence.Dispose();
        }

        public override void OnGameStarted(OnGameStartedEventArgs args)
        {
            var started = new RunningGame
            {
                Game = args.Game,
                StartedUtc = DateTime.UtcNow,
                ExecutablePath = GameProcess.GetPath(args.StartedProcessId)
            };

            // The started process may be a launcher; only trust it if it lives in the game folder.
            if (GameProcess.IsUnder(started.ExecutablePath, args.Game.InstallDirectory))
            {
                started.ProcessId = args.StartedProcessId;
            }

            lock (runningGames)
            {
                runningGames.RemoveAll(r => r.Game.Id == args.Game.Id);
                runningGames.Add(started);
            }

            RefreshPresence();
            if (started.ProcessId == 0)
            {
                _ = LocateGameProcessAsync(started);
            }
        }

        /// <summary>
        /// Reporting the activity under the game's own process lets Discord merge it with the
        /// game it detects by itself; otherwise Discord shows its own activity and icon instead.
        /// Launchers can take a while to start the actual game, hence the polling.
        /// </summary>
        private async Task LocateGameProcessAsync(RunningGame running)
        {
            for (var attempt = 0; attempt < 36; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                lock (runningGames)
                {
                    if (!runningGames.Contains(running))
                    {
                        return;
                    }
                }

                var found = GameProcess.FindIn(running.Game.InstallDirectory);
                if (found != null)
                {
                    running.ProcessId = found.Id;
                    running.ExecutablePath = found.Path;
                    RefreshPresence();
                    return;
                }
            }
        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            lock (runningGames)
            {
                runningGames.RemoveAll(r => r.Game.Id == args.Game.Id);
            }

            RefreshPresence();
        }

        internal void RefreshPresence()
        {
            if (!Settings.Enabled)
            {
                presence.Set(null);
                return;
            }

            RunningGame current;
            lock (runningGames)
            {
                current = runningGames.LastOrDefault(r => !Settings.HiddenGames.Contains(r.Game.Id));
            }

            var builder = new PresenceBuilder(Settings, Catalog, PlayniteImageUrl, LibraryName);
            if (current != null)
            {
                presence.Set(builder.ForGame(current));
                UploadChosenImageIfNeeded(current.Game);
            }
            else if (Settings.ShowIdlePresence && mainWindowShown)
            {
                presence.Set(builder.Idle(PlayniteApi.Database.Games.Where(g => !g.Hidden).ToList()));
            }
            else
            {
                presence.Set(null);
            }
        }

        private string LibraryName(Guid pluginId)
        {
            return PlayniteApi.Addons.Plugins.OfType<LibraryPlugin>().FirstOrDefault(p => p.Id == pluginId)?.Name;
        }

        private string PlayniteImageUrl(string image)
        {
            if (PresenceBuilder.IsHttpUrl(image))
            {
                return image;
            }

            return Settings.UploadLocalImages ? imageHost.TryGetUrl(PlayniteApi.Database.GetFullFilePath(image)) : null;
        }

        /// <summary>
        /// The presence goes out right away with a fallback image; once the chosen
        /// image is hosted, it is refreshed with it. Later sessions use the cached URL.
        /// </summary>
        private void UploadChosenImageIfNeeded(Playnite.SDK.Models.Game game)
        {
            var image = PresenceBuilder.ChosenImage(game, Settings.GameImage);
            if (!Settings.ShowGameImage || !Settings.UploadLocalImages || string.IsNullOrEmpty(Settings.ImgbbApiKey)
                || string.IsNullOrEmpty(image) || PresenceBuilder.IsHttpUrl(image))
            {
                return;
            }

            var path = PlayniteApi.Database.GetFullFilePath(image);
            if (imageHost.TryGetUrl(path) != null || !File.Exists(path))
            {
                return;
            }

            imageHost.UploadAsync(path, Settings.ImgbbApiKey).ContinueWith(upload =>
            {
                if (upload.Result != null)
                {
                    RefreshPresence();
                }
            });
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            var games = args.Games;
            var allHidden = games.All(g => Settings.HiddenGames.Contains(g.Id));
            yield return new GameMenuItem
            {
                MenuSection = MenuSection,
                Description = allHidden ? "Share activity on Discord" : "Hide activity from Discord",
                Action = menuArgs =>
                {
                    foreach (var game in games)
                    {
                        if (allHidden)
                        {
                            Settings.HiddenGames.Remove(game.Id);
                        }
                        else
                        {
                            Settings.HiddenGames.Add(game.Id);
                        }
                    }

                    SaveAndRefresh();
                }
            };

            if (games.Count != 1)
            {
                yield break;
            }

            var single = games[0];
            yield return new GameMenuItem
            {
                MenuSection = MenuSection,
                Description = "Set Discord application ID…",
                Action = menuArgs =>
                {
                    Settings.ApplicationOverrides.TryGetValue(single.Id, out var current);
                    var suggestion = current ?? Catalog.Find(single.Name, null, PresenceBuilder.SteamAppId(single))?.Id ?? string.Empty;
                    var result = PlayniteApi.Dialogs.SelectString(
                        "Discord application ID to use for this game.\nLeave empty to detect it automatically.",
                        single.Name,
                        suggestion);
                    if (!result.Result)
                    {
                        return;
                    }

                    var id = result.SelectedString.Trim();
                    if (id.Length == 0)
                    {
                        Settings.ApplicationOverrides.Remove(single.Id);
                    }
                    else if (ulong.TryParse(id, out _))
                    {
                        Settings.ApplicationOverrides[single.Id] = id;
                    }
                    else
                    {
                        PlayniteApi.Dialogs.ShowErrorMessage("A Discord application ID is a number.", MenuSection);
                        return;
                    }

                    SaveAndRefresh();
                }
            };
        }

        public override ISettings GetSettings(bool firstRunSettings) => settingsViewModel;

        public override UserControl GetSettingsView(bool firstRunSettings) => new BetterRichPresenceSettingsView();

        private void SaveAndRefresh()
        {
            SavePluginSettings(Settings);
            RefreshPresence();
        }

        private void WarnIfBuiltInPresenceEnabled()
        {
            try
            {
                var config = Path.Combine(PlayniteApi.Paths.ConfigurationPath, "config.json");
                if (File.Exists(config) && File.ReadAllText(config).Contains("\"DiscordPresenceEnabled\": true"))
                {
                    PlayniteApi.Notifications.Add(new NotificationMessage(
                        "BetterRichPresence_BuiltIn",
                        "Better Rich Presence: Playnite's built-in Discord presence is also enabled, so Discord may show two activities. Close Playnite and set \"DiscordPresenceEnabled\" to false in config.json (Playnite has no option for it in its settings).",
                        NotificationType.Info));
                }
            }
            catch (Exception e)
            {
                logger.Warn(e, "Could not read Playnite configuration");
            }
        }
    }
}
