using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace VoidUI.Services
{

    public enum UpdateStage
    {
        Idle,
        Checking,
        UpToDate,
        Available,
        Downloading,
        Downloaded,
        Failed
    }


    ///







    public class UpdateService
    {
        private const string RepoOwner = "zeuthe";
        private const string RepoName = "void-ui";

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

        private CancellationTokenSource _cts;

        public UpdateStage Stage { get; private set; } = UpdateStage.Idle;


        public string LatestVersion { get; private set; } = "";


        public string ReleaseTag { get; private set; } = "";


        private string _assetUrl = "";

        public string ReleaseNotes { get; private set; } = "";


        public string DownloadedPath { get; private set; } = "";


        public int Progress { get; private set; }

        public string Error { get; private set; } = "";


        public event Action StateChanged;


        public static string CurrentVersion
        {
            get
            {
                try
                {
                    var v = Assembly.GetExecutingAssembly().GetName().Version;
                    return v == null ? "1.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
                }
                catch { return "1.0.0"; }
            }
        }


        public bool HasNotice => Stage is UpdateStage.Available or UpdateStage.Downloading or UpdateStage.Downloaded;

        public bool IsInstalled => Stage == UpdateStage.Downloaded && File.Exists(DownloadedPath);






        public void StartCheck(bool autoDownload)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            _ = RunCheckAsync(autoDownload, _cts.Token);
        }


        public void Cancel()
        {
            _cts?.Cancel();
        }

        private async Task RunCheckAsync(bool autoDownload, CancellationToken ct)
        {
            try
            {
                Set(UpdateStage.Checking);

                using var req = new HttpRequestMessage(HttpMethod.Get,
                    $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest");

                req.Headers.UserAgent.ParseAdd("SweetyVoid-Updater");
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

                using var resp = await Http.SendAsync(req, ct);
                resp.EnsureSuccessStatusCode();

                var json = JObject.Parse(await resp.Content.ReadAsStringAsync(ct));

                ReleaseTag = json.Value<string>("tag_name") ?? json.Value<string>("name") ?? "";
                LatestVersion = NormalizeTag(ReleaseTag);
                ReleaseNotes = json.Value<string>("body") ?? "";

                var asset = SelectInstallerAsset(json["assets"] as JArray);
                _assetUrl = asset?.Value<string>("browser_download_url") ?? "";
                if (asset == null || !IsNewerThan(LatestVersion))
                {
                    Error = "";
                    Set(UpdateStage.UpToDate);
                    return;
                }

                if (autoDownload)
                    await DownloadAssetAsync(asset.Value<string>("browser_download_url"),
                                             asset.Value<string>("name"), ct);
                else
                    Set(UpdateStage.Available);
            }
            catch (OperationCanceledException)
            {

            }
            catch (Exception ex)
            {
                Error = ex.Message;
                Set(UpdateStage.Failed);
            }
        }




        public void StartDownload()
        {
            if (string.IsNullOrEmpty(LatestVersion)) return;

            _cts ??= new CancellationTokenSource();
            var token = _cts.Token;
            _ = Task.Run(async () =>
            {
                try
                {


                    var url = _assetUrl;
                    if (string.IsNullOrEmpty(url))
                        url = $"https://github.com/{RepoOwner}/{RepoName}/releases/download/" +
                              $"{ReleaseTag}/VoidUI.Setup.exe";

                    await DownloadAssetAsync(url, $"VoidUI.Setup.{LatestVersion}.exe", token);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Error = ex.Message;
                    Set(UpdateStage.Failed);
                }
            }, token);
        }

        private async Task DownloadAssetAsync(string url, string fileName, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(url)) throw new InvalidOperationException("release asset url is empty");

            Set(UpdateStage.Downloading, progress: 0);

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VoidUI", "updates");
            Directory.CreateDirectory(dir);

            var dest = Path.Combine(dir, string.IsNullOrWhiteSpace(fileName) ? "VoidUI.Setup.exe" : fileName);

            using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            var total = resp.Content.Headers.ContentLength ?? -1;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(dest);

            var buffer = new byte[81920];
            long totalRead = 0;
            var lastPct = -1;

            long read;
            while ((read = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, (int)read), ct);
                totalRead += read;

                if (total <= 0) continue;
                var pct = (int)(totalRead * 100 / total);
                if (pct == lastPct) continue;
                lastPct = pct;
                Set(UpdateStage.Downloading, progress: pct);
            }

            DownloadedPath = dest;
            Error = "";
            Set(UpdateStage.Downloaded, progress: 100);
        }







        public bool TryInstall()
        {
            if (!IsInstalled) return false;

            try
            {
                var installDir = FindInstallDir();
                var args = $"/c ping 127.0.0.1 -n 3 >nul & \"{DownloadedPath}\"" +
                           " --silent --dir \"" + installDir + "\" --launch";

                Process.Start(new ProcessStartInfo("cmd.exe", args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                return true;
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                Set(UpdateStage.Failed);
                return false;
            }
        }



        private static string FindInstallDir()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SweetyVoid");
                var reg = key?.GetValue("InstallLocation") as string;
                if (!string.IsNullOrWhiteSpace(reg)) return reg;
            }
            catch {  }

            var baseDir = AppContext.BaseDirectory;
            return string.IsNullOrWhiteSpace(baseDir) ? @"C:\Program Files\Sweety Void" : baseDir.TrimEnd('\\');
        }


        public void ResetToIdle()
        {
            try
            {
                if (File.Exists(DownloadedPath)) File.Delete(DownloadedPath);
            }
            catch {  }

            DownloadedPath = "";
            Progress = 0;
            Set(UpdateStage.Idle);
        }



        private static JToken SelectInstallerAsset(JArray assets)
        {
            if (assets == null || assets.Count == 0) return null;

            var exe = assets.Where(a => (a.Value<string>("name") ?? "")
                                        .EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            .ToList();
            if (exe.Count == 0) return null;


            return exe.FirstOrDefault(a => (a.Value<string>("name") ?? "")
                                           .Contains("setup", StringComparison.OrdinalIgnoreCase))
                   ?? exe[0];
        }

        private static string NormalizeTag(string tag) =>
            TryReadVersion(tag, out var v) ? v.ToString() : (tag ?? "").Trim().TrimStart('v', 'V');




        private static bool TryReadVersion(string? text, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var m = System.Text.RegularExpressions.Regex.Match(text, @"\d+(?:\.\d+)*");
            if (!m.Success) return false;

            var raw = m.Value;
            if (!raw.Contains('.'))
            {

                if (raw.Length > 4) return false;
                raw = string.Join('.', raw.PadRight(3, '0').ToCharArray(), 0, 3);
            }

            return Version.TryParse(raw, out version);
        }

        private static bool IsNewerThan(string latest) =>
            TryReadVersion(latest, out var next) &&
            Version.TryParse(CurrentVersion, out var cur) &&
            next > cur;

        private void Set(UpdateStage stage, int? progress = null)
        {
            Stage = stage;
            if (progress.HasValue) Progress = progress.Value;
            StateChanged?.Invoke();
        }
    }
}
