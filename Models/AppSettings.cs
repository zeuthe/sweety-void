using System.Collections.Generic;
using Newtonsoft.Json;

namespace VoidUI.Models
{
    public class AppSettings
    {
        public CrosshairState Crosshair { get; set; } = new();



        [JsonProperty("Tracker")]
        public TrackerSettings Tracker { get; set; } = new();


        public ThemeSettings CustomTheme { get; set; } = new();

        public string Theme { get; set; } = "black";



        public string UpdateMode { get; set; } = "ask";


        // Global style overrides — empty value keeps whatever the active theme defines.
        public string UiFont { get; set; } = "";
        public string UiButtonMode { get; set; } = "";
    }

    public class TrackerSettings
    {
        public List<TrackedSite> CustomSites { get; set; } = new();


        public string ParseMode { get; set; } = "permanent";


        public int IntervalHours { get; set; } = 0;
        public int IntervalMinutes { get; set; } = 0;
        public int IntervalSeconds { get; set; } = 30;


        public int IntervalSpread { get; set; } = 5;
    }

    public class TrackedSite
    {
        public string Name { get; set; } = "";
        public string UrlTemplate { get; set; } = "";
    }
}
