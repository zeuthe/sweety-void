using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VoidUI.Models;

namespace VoidUI.Services
{
    public class SettingsService
    {
        private readonly string _settingsPath;
        private AppSettings _settings;

        public SettingsService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var voidUiPath = Path.Combine(appData, "VoidUI");
            Directory.CreateDirectory(voidUiPath);
            _settingsPath = Path.Combine(voidUiPath, "settings.json");
            _settings = Load();
        }

        public AppSettings GetSettings() => _settings;

        public AppSettings Load()
        {
            if (File.Exists(_settingsPath))
            {
                try
                {
                    var json = File.ReadAllText(_settingsPath);
                    _settings = JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
                    MigrateLegacySectionName(json);
                }
                catch { _settings = new AppSettings(); }
            }
            else
            {
                _settings = new AppSettings();
            }
            return _settings;
        }



        private void MigrateLegacySectionName(string json)
        {
            if (_settings.Tracker != null && _settings.Tracker.CustomSites.Count > 0) return;

            try
            {
                var legacy = (JObject?)JObject.Parse(json)["Combat"];
                var tracker = legacy?.ToObject<TrackerSettings>();
                if (tracker != null) _settings.Tracker = tracker;
            }
            catch {  }
        }

        private readonly object _saveLock = new();

        public void Save(AppSettings settings)
        {
            lock (_saveLock)
            {
                _settings = settings;
                var json = JsonConvert.SerializeObject(settings, Formatting.Indented);





                var tmp = _settingsPath + ".tmp";
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        File.WriteAllText(tmp, json);
                        if (File.Exists(_settingsPath))
                            File.Replace(tmp, _settingsPath, null, true);
                        else
                            File.Move(tmp, _settingsPath);
                        return;
                    }
                    catch (Exception ex) when (attempt < 4 && ex is IOException or UnauthorizedAccessException)
                    {
                        System.Threading.Thread.Sleep(25);
                    }
                    catch (Exception ex)
                    {
                        try { File.Delete(tmp); } catch { }
                        CrashLog.Write("Settings.Save", ex);
                        return;
                    }
                }
            }
        }

        public void SaveCrosshair(CrosshairState crosshair)
        {
            _settings.Crosshair = crosshair;
            Save(_settings);
        }

        public void SetTheme(string theme)
        {
            _settings.Theme = theme;
            Save(_settings);
        }
    }
}
