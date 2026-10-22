using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;

namespace BetterRichPresence
{
    public enum StatusDisplay
    {
        /// <summary>Member list shows "Playing &lt;application name&gt;".</summary>
        Name = 0,
        /// <summary>Member list shows the state line.</summary>
        State = 1,
        /// <summary>Member list shows the details line.</summary>
        Details = 2
    }

    public enum GameImageKind
    {
        Icon,
        Cover
    }

    /// <summary>
    /// Default values of every setting live in the field initializers of this class:
    /// change them here to change what new users get.
    /// </summary>
    public class BetterRichPresenceSettings : ObservableObject
    {
        /// <summary>
        /// Playnite's own Discord application, used when Discord doesn't know the game. Unlike a new
        /// application, Discord shows its icon next to the user's name instead of a "?".
        /// </summary>
        public const string DefaultApplicationId = "689105200262414377";

        /// <summary>Asset key uploaded to the plugin's Discord application.</summary>
        public const string DefaultFallbackImage = "playnite-avatar";

        /// <summary>
        /// The Playnite logo uploaded to the plugin's own Discord application, served by Discord's CDN
        /// so it shows under any application, including games Discord knows.
        /// </summary>
        public const string PlayniteLogoUrl = "https://cdn.discordapp.com/app-assets/1562799211265663018/1562800353509122280.png";

        private bool enabled = true;
        public bool Enabled { get => enabled; set => SetValue(ref enabled, value); }

        private bool useGameApplications = true;
        public bool UseGameApplications { get => useGameApplications; set => SetValue(ref useGameApplications, value); }

        private string fallbackApplicationId = DefaultApplicationId;
        public string FallbackApplicationId { get => fallbackApplicationId; set => SetValue(ref fallbackApplicationId, value?.Trim()); }

        // An asset key of the fallback application, or an image URL.
        private string fallbackImage = DefaultFallbackImage;
        public string FallbackImage { get => fallbackImage; set => SetValue(ref fallbackImage, value?.Trim()); }

        // Used when Discord knows the game: the activity title already is the game name.
        private string matchedDetails = "{source} - {platform}";
        public string MatchedDetails { get => matchedDetails; set => SetValue(ref matchedDetails, value); }

        private string matchedState = "{playtime} played";
        public string MatchedState { get => matchedState; set => SetValue(ref matchedState, value); }

        // Used with the fallback application: the title is "Playnite", so the name must appear here.
        private string fallbackDetails = "{name}";
        public string FallbackDetails { get => fallbackDetails; set => SetValue(ref fallbackDetails, value); }

        private string fallbackState = "{source} · {platform}";
        public string FallbackState { get => fallbackState; set => SetValue(ref fallbackState, value); }

        private StatusDisplay matchedStatusDisplay = StatusDisplay.Name;
        public StatusDisplay MatchedStatusDisplay { get => matchedStatusDisplay; set => SetValue(ref matchedStatusDisplay, value); }

        private StatusDisplay fallbackStatusDisplay = StatusDisplay.Name;
        public StatusDisplay FallbackStatusDisplay { get => fallbackStatusDisplay; set => SetValue(ref fallbackStatusDisplay, value); }

        private bool showElapsedTime = true;
        public bool ShowElapsedTime { get => showElapsedTime; set => SetValue(ref showElapsedTime, value); }

        private bool showGameImage = true;
        public bool ShowGameImage { get => showGameImage; set => SetValue(ref showGameImage, value); }

        private bool showPlayniteBadge = true;
        public bool ShowPlayniteBadge { get => showPlayniteBadge; set => SetValue(ref showPlayniteBadge, value); }

        private GameImageKind gameImage = GameImageKind.Icon;
        public GameImageKind GameImage { get => gameImage; set => SetValue(ref gameImage, value); }

        // Nothing is uploaded until the user also enters their own ImgBB key.
        private bool uploadLocalImages = true;
        public bool UploadLocalImages { get => uploadLocalImages; set => SetValue(ref uploadLocalImages, value); }

        // Never ship a default key: everyone's uploads would land on, and use the quota of, that account.
        private string imgbbApiKey = string.Empty;
        public string ImgbbApiKey { get => imgbbApiKey; set => SetValue(ref imgbbApiKey, value?.Trim()); }

        private bool showIdlePresence = true;
        public bool ShowIdlePresence { get => showIdlePresence; set => SetValue(ref showIdlePresence, value); }

        private string idleDetails = "Browsing the library";
        public string IdleDetails { get => idleDetails; set => SetValue(ref idleDetails, value); }

        private string idleState = "{games} games";
        public string IdleState { get => idleState; set => SetValue(ref idleState, value); }

        /// <summary>Games whose activity is never shared.</summary>
        public HashSet<Guid> HiddenGames { get; set; } = new HashSet<Guid>();

        /// <summary>Per-game Discord application ID chosen by the user.</summary>
        public Dictionary<Guid, string> ApplicationOverrides { get; set; } = new Dictionary<Guid, string>();
    }

    public class BetterRichPresenceSettingsViewModel : ObservableObject, ISettings
    {
        private readonly BetterRichPresencePlugin plugin;
        private BetterRichPresenceSettings editingClone;

        private BetterRichPresenceSettings settings;
        public BetterRichPresenceSettings Settings { get => settings; set => SetValue(ref settings, value); }

        private string catalogStatus;
        [DontSerialize]
        public string CatalogStatus { get => catalogStatus; set => SetValue(ref catalogStatus, value); }

        public RelayCommand RefreshCatalogCommand { get; }
        public RelayCommand ResetDiscordApplicationCommand { get; }

        public BetterRichPresenceSettingsViewModel(BetterRichPresencePlugin plugin)
        {
            this.plugin = plugin;
            Settings = plugin.LoadPluginSettings<BetterRichPresenceSettings>() ?? new BetterRichPresenceSettings();
            RefreshCatalogCommand = new RelayCommand(async () =>
            {
                CatalogStatus = "Downloading…";
                await plugin.Catalog.LoadAsync(forceRefresh: true);
                UpdateCatalogStatus();
            });
            ResetDiscordApplicationCommand = new RelayCommand(() =>
            {
                Settings.FallbackApplicationId = BetterRichPresenceSettings.DefaultApplicationId;
                Settings.FallbackImage = BetterRichPresenceSettings.DefaultFallbackImage;
            });
        }

        public void BeginEdit()
        {
            editingClone = Serialization.GetClone(Settings);
            UpdateCatalogStatus();
        }

        public void CancelEdit()
        {
            Settings = editingClone;
        }

        public void EndEdit()
        {
            plugin.SavePluginSettings(Settings);
            plugin.RefreshPresence();
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            if (!ulong.TryParse(Settings.FallbackApplicationId, out _))
            {
                errors.Add("The fallback Discord application ID must be a number.");
            }

            return errors.Count == 0;
        }

        private void UpdateCatalogStatus()
        {
            CatalogStatus = $"{plugin.Catalog.Count} games known by Discord";
        }
    }
}
