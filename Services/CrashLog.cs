using System;
using System.IO;

namespace VoidUI.Services
{
    /// <summary>



    /// </summary>
    public static class CrashLog
    {
        private static readonly object Lock = new();

        public static void Write(string where, Exception ex)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "VoidUI");
                Directory.CreateDirectory(dir);
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {where}: {ex}\r\n\r\n";
                lock (Lock)
                    File.AppendAllText(Path.Combine(dir, "crash.log"), line);
            }
            catch { }
        }
    }
}
