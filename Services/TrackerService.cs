using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VoidUI.Models;

namespace VoidUI.Services
{
    public class TrackerService
    {
        private readonly SteamService _steamService;
        private CancellationTokenSource _monitorCts;
        private readonly HashSet<string> _processedDeaths = new();
        private readonly List<ServerInfo> _servers = new();
        private readonly Dictionary<string, string> _serverNameCache = new();
        private readonly Dictionary<string, string> _avatarCache = new();
        private string _currentServerIp = "";
        private long _lastReadPosition;
        private static readonly HttpClient _http = new();

        // ---------- combatlog (F1) ----------

        private readonly List<CombatLogEntry> _combatLog = new();


        private readonly HashSet<string> _seenCombatLines = new();


        private int[]? _combatCols;

        private const int MaxCombatLog = 400;


        private static readonly Regex LogPrefixRx = new(
            @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?Z\|0x[0-9a-fA-F]+\|",
            RegexOptions.Compiled);


        private static readonly Regex CombatTimeRx = new(
            @"^\d+(\.\d+)?s\s",
            RegexOptions.Compiled);

        public event Action<List<ServerInfo>> OnDataUpdated;
        public event Action<string> OnStatusChanged;



        public event Action<string> OnParseFinished;



        public const string ModeOnce = "once";

        public const string ModeInterval = "interval";

        public const string ModePermanent = "permanent";

        private static readonly Random _rng = new();


        public string ParseMode { get; private set; } = ModePermanent;


        public double IntervalSeconds { get; private set; } = 30;
        public int IntervalSpread { get; private set; } = 5;


        public bool IsMonitoring => _monitorCts != null;

        public TrackerService(SteamService steamService)
        {
            _steamService = steamService;
        }

        public void StartMonitoring(string mode, double intervalSeconds, int spreadSeconds)
        {
            if (_monitorCts != null) return;

            ParseMode = mode;
            IntervalSeconds = Math.Max(1.0, intervalSeconds);
            IntervalSpread = Math.Max(0, spreadSeconds);

            _lastReadPosition = 0;
            var cts = new CancellationTokenSource();
            _monitorCts = cts;
            Task.Run(() => MonitorLoop(cts));
            OnStatusChanged?.Invoke(StatusFor(mode));
            OnDataUpdated?.Invoke(_servers);
        }


        public void UpdateInterval(double intervalSeconds, int spreadSeconds)
        {
            IntervalSeconds = Math.Max(1.0, intervalSeconds);
            IntervalSpread = Math.Max(0, spreadSeconds);
        }

        public void StopMonitoring()
        {


            _monitorCts?.Cancel();
        }

        private static string StatusFor(string mode) => mode switch
        {
            ModeOnce => "One-shot",
            ModeInterval => "Interval",
            _ => "Monitoring"
        };


        private int NextDelayMs()
        {
            if (ParseMode != ModeInterval) return 3000;


            double jitter = IntervalSpread > 0
                ? (_rng.NextDouble() * 2.0 - 1.0) * IntervalSpread
                : 0.0;
            double delay = Math.Max(1.0, IntervalSeconds + jitter);
            return (int)(delay * 1000.0);
        }

        private async Task MonitorLoop(CancellationTokenSource cts)
        {
            var token = cts.Token;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        ProcessLog();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Tracker monitoring error: {ex.Message}");
                    }

                    if (ParseMode == ModeOnce) break;

                    try { await Task.Delay(NextDelayMs(), token); }
                    catch (OperationCanceledException) { break; }
                }
            }
            finally
            {

                if (_monitorCts == cts) _monitorCts = null;
                OnStatusChanged?.Invoke("Stopped");
                OnParseFinished?.Invoke(ParseMode);
                OnDataUpdated?.Invoke(_servers);
            }
        }

        private void ProcessLog()
        {
            var logPath = _steamService.GetRustLogPath();
            if (string.IsNullOrEmpty(logPath) || !File.Exists(logPath)) return;

            try
            {
                using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                stream.Seek(_lastReadPosition, SeekOrigin.Begin);

                if (stream.Position >= stream.Length) return;

                using var reader = new StreamReader(stream);
                var content = reader.ReadToEnd();
                _lastReadPosition = stream.Position;

                if (string.IsNullOrEmpty(content)) return;

                ParseServerConnections(content);
                ParseDeathEvents(content);
                ParseCombatLog(content);
                OnDataUpdated?.Invoke(_servers);
            }
            catch { }
        }

        private void ParseServerConnections(string content)
        {
            var connectRegex = new Regex(@"Connecting:\s*(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}):(\d+)\s*\(Raknet\)", RegexOptions.Compiled);

            var connectMatches = connectRegex.Matches(content).Cast<Match>().ToList();

            foreach (var match in connectMatches)
            {
                var ip = match.Groups[1].Value;
                var port = match.Groups[2].Value;
                var serverIp = $"{ip}:{port}";
                var key = $"{ip}_{port}";

                _currentServerIp = serverIp;

                if (!string.IsNullOrEmpty(serverIp) && !_servers.Any(s => s.ServerIp == serverIp))
                {
                    var server = new ServerInfo
                    {
                        ServerName = _serverNameCache.TryGetValue(key, out var cached) ? cached : serverIp,
                        ServerIp = serverIp
                    };
                    _servers.Insert(0, server);

                    _ = ResolveServerNameAsync(ip, int.Parse(port), key, server);
                }
            }
        }

        private async Task ResolveServerNameAsync(string ip, int port, string key, ServerInfo server)
        {
            try
            {
                var url = $"https://api.steampowered.com/ISteamApps/GetServersAtAddress/v1/?format=json&addr={ip}:{port}";
                var response = await _http.GetStringAsync(url);
                var nameMatch = Regex.Match(response, "\"servername\"\\s*:\\s*\"([^\"]+)\"");
                if (nameMatch.Success)
                {
                    var name = nameMatch.Groups[1].Value;
                    _serverNameCache[key] = name;
                    server.ServerName = name;
                    OnDataUpdated?.Invoke(_servers);
                }
            }
            catch
            {
                server.ServerName = $"{ip}:{port}";
            }
        }

        private async Task FetchAvatarAsync(string steamId)
        {
            try
            {
                var url = $"https://steamcommunity.com/profiles/{steamId}?xml=1";
                var response = await _http.GetStringAsync(url);
                var avatarMatch = Regex.Match(response, @"<avatarFull><!\[CDATA\[(.*?)\]\]></avatarFull>");
                if (!avatarMatch.Success)
                    avatarMatch = Regex.Match(response, @"<avatarFull>(.*?)</avatarFull>");
                if (avatarMatch.Success)
                {
                    var avatarUrl = avatarMatch.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(avatarUrl))
                    {
                        _avatarCache[steamId] = avatarUrl;
                        foreach (var s in _servers)
                            foreach (var t in s.Targets)
                                if (t.SteamId == steamId) t.AvatarUrl = avatarUrl;
                        OnDataUpdated?.Invoke(_servers);
                    }
                }
            }
            catch { }
        }

        private void ParseDeathEvents(string content)
        {
            var deathRegex = new Regex(
                @"(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+Z)\|0x[0-9a-fA-F]+\|You died:\s*(.*)",
                RegexOptions.Compiled);

            var playerKillRegex = new Regex(
                @"^killed by\s+(.+?)\s*\((\d{17,})\)\s*$",
                RegexOptions.Compiled);

            var npcKillRegex = new Regex(
                @"^killed by\s+(.+?)\s*$",
                RegexOptions.Compiled);

            foreach (Match match in deathRegex.Matches(content))
            {
                var timestamp = match.Groups[1].Value;
                var deathType = match.Groups[2].Value.Trim();

                var deathKey = $"{timestamp}_{deathType}";
                if (_processedDeaths.Contains(deathKey)) continue;
                _processedDeaths.Add(deathKey);

                string killerName;
                string killerSteamId;
                bool isPlayer;

                var playerMatch = playerKillRegex.Match(deathType);
                if (playerMatch.Success)
                {
                    killerName = playerMatch.Groups[1].Value;
                    killerSteamId = playerMatch.Groups[2].Value;
                    isPlayer = true;
                }
                else
                {
                    var npcMatch = npcKillRegex.Match(deathType);
                    if (npcMatch.Success)
                    {
                        killerName = npcMatch.Groups[1].Value;
                    }
                    else
                    {
                        killerName = deathType;
                    }
                    killerSteamId = "";
                    isPlayer = false;
                }

                var isoTime = timestamp.Replace("Z", "+00:00");
                if (DateTimeOffset.TryParse(isoTime, out var dto))
                {
                    var localTime = dto.ToLocalTime();
                    timestamp = localTime.ToString("yyyy-MM-dd HH:mm:ss");
                }

                var server = _servers.FirstOrDefault(s => s.ServerIp == _currentServerIp);
                if (server == null)
                {
                    server = new ServerInfo
                    {
                        ServerName = _currentServerIp,
                        ServerIp = _currentServerIp
                    };
                    _servers.Add(server);
                }

                if (isPlayer && !string.IsNullOrEmpty(killerSteamId) && !_avatarCache.ContainsKey(killerSteamId))
                {
                    _ = FetchAvatarAsync(killerSteamId);
                }

                var existingTarget = server.Targets.FirstOrDefault(t =>
                    (isPlayer && t.SteamId == killerSteamId) ||
                    (!isPlayer && t.PlayerName == killerName && t.SteamId == ""));

                if (existingTarget != null)
                {
                    existingTarget.TimesKilledMe++;
                    existingTarget.LastDeathTime = timestamp;
                    if (isPlayer && _avatarCache.TryGetValue(killerSteamId, out var av))
                        existingTarget.AvatarUrl = av;
                    existingTarget.DeathHistory.Add(new DeathEvent
                    {
                        Timestamp = timestamp,
                        KillerName = killerName,
                        KillerSteamId = killerSteamId,
                        IsPlayer = isPlayer
                    });
                }
                else
                {
                    var avatarUrl = (isPlayer && _avatarCache.TryGetValue(killerSteamId, out var av)) ? av : "";
                    var newTarget = new TargetPlayer
                    {
                        PlayerName = killerName,
                        SteamId = killerSteamId,
                        AvatarUrl = avatarUrl,
                        LastDeathTime = timestamp,
                        TimesKilledMe = 1,
                        DeathHistory = new List<DeathEvent>
                        {
                            new()
                            {
                                Timestamp = timestamp,
                                KillerName = killerName,
                                KillerSteamId = killerSteamId,
                                IsPlayer = isPlayer
                            }
                        }
                    };
                    server.Targets.Add(newTarget);
                }
            }

            if (_processedDeaths.Count > 500)
            {
                var keep = _processedDeaths.TakeLast(250).ToList();
                _processedDeaths.Clear();
                foreach (var k in keep) _processedDeaths.Add(k);
            }
        }


        public IReadOnlyList<CombatLogEntry> CombatLog => _combatLog;


        public void ClearCombatLog()
        {
            _combatLog.Clear();
            _seenCombatLines.Clear();

        }





        private void ParseCombatLog(string content)
        {
            foreach (var raw in content.Split('\n'))
            {
                var line = StripLogPrefix(raw).TrimEnd();
                if (line.Length == 0) continue;

                if (line.Contains("attacker", StringComparison.OrdinalIgnoreCase)
                    && line.Contains("target", StringComparison.OrdinalIgnoreCase)
                    && line.Contains("weapon", StringComparison.OrdinalIgnoreCase))
                {
                    _combatCols = ColumnOffsets(line);
                    continue;
                }

                if (_combatCols == null || _combatCols.Length < 10) continue;
                if (!CombatTimeRx.IsMatch(line)) continue;
                if (!_seenCombatLines.Add(line)) continue;

                _combatLog.Add(ReadCombatRow(line, _combatCols));
            }

            if (_combatLog.Count > MaxCombatLog)
            {
                var drop = _combatLog.Count - MaxCombatLog;
                for (var i = 0; i < drop; i++) _seenCombatLines.Remove(_combatLog[i].Raw);
                _combatLog.RemoveRange(0, drop);
            }
        }


        private static string StripLogPrefix(string line)
        {
            var m = LogPrefixRx.Match(line);
            return m.Success ? line.Substring(m.Length) : line;
        }

        private static int[] ColumnOffsets(string header)
        {
            var offsets = new List<int>();
            foreach (Match m in Regex.Matches(header, @"\S+")) offsets.Add(m.Index);
            return offsets.ToArray();
        }


        /// distance old_hp new_hp info …</summary>
        private static CombatLogEntry ReadCombatRow(string line, int[] cols) => new()
        {
            Raw = line,
            Time = SliceField(line, cols, 0),
            Attacker = SliceField(line, cols, 1),
            Target = SliceField(line, cols, 2),
            Weapon = ShortWeapon(SliceField(line, cols, 3)),
            Ammo = SliceField(line, cols, 4),
            Area = SliceField(line, cols, 5),
            Distance = SliceField(line, cols, 6),
            OldHp = SliceField(line, cols, 7),
            NewHp = SliceField(line, cols, 8),
            Info = SliceField(line, cols, 9)
        };

        private static string SliceField(string line, int[] cols, int index)
        {
            if (index >= cols.Length) return "";
            var start = cols[index];
            if (start >= line.Length) return "";
            var end = index + 1 < cols.Length ? cols[index + 1] : line.Length;
            if (end > line.Length) end = line.Length;
            if (end <= start) return "";
            return line.Substring(start, end - start).Trim();
        }

        /// <summary>«assets/prefabs/weapons/stone hatchet/stonehatchet.entity.prefab» → «stonehatchet».</summary>
        private static string ShortWeapon(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Equals("N/A", StringComparison.OrdinalIgnoreCase))
                return "N/A";

            var name = path.Replace('\\', '/');
            var slash = name.LastIndexOf('/');
            if (slash >= 0) name = name.Substring(slash + 1);

            foreach (var suffix in new[] { ".entity.prefab", ".prefab" })
            {
                var idx = name.LastIndexOf(suffix, StringComparison.OrdinalIgnoreCase);
                if (idx > 0) { name = name.Substring(0, idx); break; }
            }

            return name;
        }
    }
}
