using System;

namespace VoidUI.Models
{

    public class CombatLogEntry
    {

        public string Raw { get; set; } = "";


        public string Time { get; set; } = "";


        public double Seconds
        {
            get
            {
                var t = Time;
                if (t.EndsWith("s", StringComparison.OrdinalIgnoreCase)) t = t.Substring(0, t.Length - 1);
                return double.TryParse(t, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0.0;
            }
        }

        public string Attacker { get; set; } = "";
        public string Target { get; set; } = "";


        public string Weapon { get; set; } = "";
        public string Ammo { get; set; } = "";
        public string Area { get; set; } = "";
        public string Distance { get; set; } = "";
        public string OldHp { get; set; } = "";
        public string NewHp { get; set; } = "";
        public string Info { get; set; } = "";

        private static bool IsYou(string value) =>
            value.Equals("you", StringComparison.OrdinalIgnoreCase);


        public bool Incoming => IsYou(Target) && !IsYou(Attacker);


        public bool Outgoing => IsYou(Attacker) && !IsYou(Target);


        public bool SelfInflicted => IsYou(Attacker) && IsYou(Target);


        public bool IsKill => Info.Contains("killed", StringComparison.OrdinalIgnoreCase);


        public bool IsDown => Info.Contains("wounded", StringComparison.OrdinalIgnoreCase);
    }
}
