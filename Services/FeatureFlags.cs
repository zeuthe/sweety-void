using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace VoidUI.Services
{

    ///




    public sealed class FeatureFlags
    {
        public bool Tracker { get; private set; } = true;

        public bool Graphics { get; private set; } = true;

        public bool Binds { get; private set; } = true;

        public bool IsEnabled(string page) => page.ToLowerInvariant() switch
        {
            "tracker" => Tracker,
            "graphics" => Graphics,
            "binds" => Binds,
            _ => true
        };

        public static FeatureFlags Load()
        {
            var flags = new FeatureFlags();
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "features.json");
                if (!File.Exists(path)) return flags;

                var json = JObject.Parse(File.ReadAllText(path));
                flags.Tracker = json.Value<bool?>("Tracker") ?? true;
                flags.Graphics = json.Value<bool?>("Graphics") ?? true;
                flags.Binds = json.Value<bool?>("Binds") ?? true;
            }
            catch {  }

            return flags;
        }
    }
}
