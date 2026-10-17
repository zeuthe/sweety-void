namespace VoidUI.Models
{


    public class ThemeSettings
    {

        public string SurfaceBase { get; set; } = "#0c0c0e";
        public string SurfaceLow { get; set; } = "#111114";
        public string SurfaceMid { get; set; } = "#18181c";
        public string SurfaceHigh { get; set; } = "#1f1f24";
        public string SurfaceOverlay { get; set; } = "#27272e";


        public string BorderSubtle { get; set; } = "#1a1a1f";
        public string BorderDefault { get; set; } = "#232329";
        public string BorderStrong { get; set; } = "#35353d";


        public string TextPrimary { get; set; } = "#ededef";
        public string TextSecondary { get; set; } = "#9d9da5";
        public string TextTertiary { get; set; } = "#5c5c66";
        public string TextDisabled { get; set; } = "#3a3a42";


        public string AccentDefault { get; set; } = "#7c5cfc";
        public string AccentHover { get; set; } = "#9178ff";
        public string AccentSubtle { get; set; } = "#2a2248";


        public string GreenDefault { get; set; } = "#34d399";
        public string RedDefault { get; set; } = "#f87171";


        public string FontFamily { get; set; } = "";


        public string ButtonMode { get; set; } = "default";


        public ThemeSettings Clone() => (ThemeSettings)MemberwiseClone();
    }
}
