using System;
using System.IO;

namespace VoidUI.Services
{
    /// <summary>
    /// User-saved presets (crosshair configs, imported anim presets) live in
    /// %LOCALAPPDATA%\VoidUI\presets, NOT in the install dir: an update
    /// extracts the new payload over the install dir and would overwrite
    /// anything stored there. On every launch missing files (e.g. presets
    /// shipped by a newer release) are copied over from the install dir —
    /// existing user files are never overwritten.
    /// </summary>
    public static class UserPresets
    {
        public static string Root { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoidUI", "presets", "crosshair");

        public static string ConfigsDir => Path.Combine(Root, "configs");
        public static string AnimPresetsDir => Path.Combine(Root, "animpresets");

        private static int _version;

        /// <summary>Bumped whenever a config/preset file is created, renamed
        /// or deleted. Consumers cache directory listings keyed by this value
        /// so a stale cache can never outlive a file change.</summary>
        public static int Version => _version;

        public static void Touch() => System.Threading.Interlocked.Increment(ref _version);

        /// <summary>One-time-per-launch sync from the legacy install-dir
        /// location. Copies new anim presets; user configs are preserved
        /// in %LOCALAPPDATA% and are not overwritten.</summary>
        public static void MigrateFromInstallDir()
        {
            // NOTE: Configs are not re-copied from install dir to preserve
            // user-created configs. Only anim presets are synced.
            try { CopyMissing("animpresets", AnimPresetsDir); } catch { }
        }

        private static void CopyMissing(string subdir, string userDir)
        {
            var src = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "presets", "crosshair", subdir);
            if (!Directory.Exists(src)) return;

            Directory.CreateDirectory(userDir);
            foreach (var file in Directory.GetFiles(src, "*.json"))
            {
                var dst = Path.Combine(userDir, Path.GetFileName(file));
                if (!File.Exists(dst)) File.Copy(file, dst);
            }
        }
    }
}
