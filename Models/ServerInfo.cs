using System.Collections.Generic;

namespace VoidUI.Models
{
    public class ServerInfo
    {
        public string ServerName { get; set; } = "";
        public string ServerIp { get; set; } = "";
        public List<TargetPlayer> Targets { get; set; } = new();
    }

    public class TargetPlayer
    {
        public string PlayerName { get; set; } = "";
        public string SteamId { get; set; } = "";
        public string AvatarUrl { get; set; } = "";
        public string LastDeathTime { get; set; } = "";
        public int TimesKilledMe { get; set; }
        public List<DeathEvent> DeathHistory { get; set; } = new();
    }

    public class DeathEvent
    {
        public string Timestamp { get; set; } = "";
        public string KillerName { get; set; } = "";
        public string KillerSteamId { get; set; } = "";
        public bool IsPlayer { get; set; }
    }
}
