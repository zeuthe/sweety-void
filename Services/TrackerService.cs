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

        // _monitorGate guards _monitorCts; _parseGate serializes log parsing so
        // a winding-down loop and a freshly started one never parse at once.
        private readonly object _monitorGate = new();
        private readonly object _parseGate = new();
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

        // player_1234 in the combat log is the Steam account id; real nicks are
        // resolved through the public profile XML and cached per session.
        private readonly Dictionary<string, string> _playerNames = new();
        private readonly HashSet<string> _playerNamesTried = new();
        private static readonly Regex PlayerIdRx = new(@"^player_(\d+)$", RegexOptions.Compiled);
        private const long SteamId64Base = 76561197960265728L;

        /// <summary>Best available display name for a combat-log actor:
        /// the resolved nick for player_1234, the raw value otherwise.</summary>
        public string DisplayName(string raw)
        {
            var steamId = SteamIdOf(raw);
            if (steamId == null) return raw;
            return _playerNames.TryGetValue(steamId, out var name) ? name : raw;
        }

        private static string? SteamIdOf(string? raw)
        {
            var m = PlayerIdRx.Match(raw ?? "");
            return m.Success ? (SteamId64Base + long.Parse(m.Groups[1].Value)).ToString() : null;
        }

        private void ResolvePlayerName(string raw)
        {
            var steamId = SteamIdOf(raw);
            if (steamId == null) return;
            if (!_playerNamesTried.Add(steamId)) return;
            _ = FetchProfileAsync(steamId);
        }


        private readonly HashSet<string> _seenCombatLines = new();


        private int[]? _combatCols;

        private const int MaxCombatLog = 400;


        private static readonly Regex LogPrefixRx = new(
            @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?Z\|0x[0-9a-fA-F]+\|",
            RegexOptions.Compiled);


        private static readonly Regex CombatTimeRx = new(
            @"^\d+(\.\d+)?s\s",
            RegexOptions.Compiled);


        // Every combat row in output_log.txt is wrapped in <color=#RRGGBB>…</color>.
        private static readonly Regex ColorTagRx = new(
            @"^<color=[^>]+>|</color>\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
            CancellationTokenSource cts;
            lock (_monitorGate)
            {
                // Idempotent: a second start while a loop is live is a no-op.
                if (_monitorCts != null) return;

                ParseMode = mode;
                IntervalSeconds = Math.Max(1.0, intervalSeconds);
                IntervalSpread = Math.Max(0, spreadSeconds);

                cts = new CancellationTokenSource();
                _monitorCts = cts;
            }

            // The position rewind happens inside the loop under _parseGate, so
            // it cannot race an in-flight parse of a loop still winding down.
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
            CancellationTokenSource cts;
            lock (_monitorGate)
            {
                // Detach first: IsMonitoring flips to false right away, so a
                // RUN click arriving before the old loop wakes up starts a
                // fresh loop instead of being swallowed by the dying one
                // (that race was the "press twice to start" bug).
                cts = _monitorCts;
                _monitorCts = null;
            }

            if (cts == null) return;
            try { cts.Cancel(); } catch { /* already disposed */ }

            // The initiator reports the stop; the loop itself only reports
            // endings it caused naturally (once-mode done or an error).
            OnStatusChanged?.Invoke("Stopped");
            OnParseFinished?.Invoke(ParseMode);
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

            // Start of a fresh run rewinds the reader (under _parseGate so it
            // cannot interleave with a still-running parse of an old loop).
            lock (_parseGate) { _lastReadPosition = 0; }

            try
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        lock (_parseGate) { ProcessLog(); }
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
                // Report only natural endings: if the loop was detached by
                // StopMonitoring (or replaced by a newer loop), that initiator
                // owns the state and a stale "Stopped" must not overwrite it.
                var stillCurrent = false;
                lock (_monitorGate)
                {
                    if (_monitorCts == cts) { _monitorCts = null; stillCurrent = true; }
                }

                if (stillCurrent)
                {
                    OnStatusChanged?.Invoke("Stopped");
                    OnParseFinished?.Invoke(ParseMode);
                }
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

        private async Task FetchAvatarAsync(string steamId) => await FetchProfileAsync(steamId);

        private async Task FetchProfileAsync(string steamId)
        {
            try
            {
                var url = $"https://steamcommunity.com/profiles/{steamId}?xml=1";
                var response = await _http.GetStringAsync(url);

                // The XML profile carries both the persona name (used to decode
                // player_1234 rows) and the full-size avatar.
                var nameMatch = Regex.Match(response, @"<steamID><!\[CDATA\[(.*?)\]\]></steamID>");
                if (!nameMatch.Success)
                    nameMatch = Regex.Match(response, @"<steamID>(.*?)</steamID>");
                if (nameMatch.Success)
                {
                    var name = nameMatch.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(name) &&
                        (!_playerNames.TryGetValue(steamId, out var old) || old != name))
                        _playerNames[steamId] = name;
                }

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
                    }
                }

                OnDataUpdated?.Invoke(_servers);
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

        // A re-entered page re-renders straight from this state instead of
        // waiting for the next parse tick ("info lost on re-entry").
        public IReadOnlyList<ServerInfo> Servers => _servers;


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

                // Rows carry a leading <color=…> tag, but Rust aligns the data
                // columns with the header as if the tag were part of the row —
                // so slice with offsets compensated by the tag length.
                var tag = ColorTagRx.Match(line);
                var shift = tag.Success ? tag.Length : 0;
                var bare = shift > 0 ? line.Substring(shift) : line;
                if (!CombatTimeRx.IsMatch(bare)) continue;
                if (!_seenCombatLines.Add(line)) continue;

                var entry = ReadCombatRow(bare, _combatCols, shift);
                _combatLog.Add(entry);

                // Kick off a one-time profile fetch per unknown player so the
                // row can show a real nick instead of player_1234.
                ResolvePlayerName(entry.Attacker);
                ResolvePlayerName(entry.Target);
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
        private static CombatLogEntry ReadCombatRow(string line, int[] cols, int shift) => new()
        {
            Raw = line,
            Time = SliceField(line, cols, 0, shift),
            Attacker = SliceField(line, cols, 1, shift),
            Target = SliceField(line, cols, 2, shift),
            Weapon = ShortWeapon(SliceField(line, cols, 3, shift)),
            Ammo = SliceField(line, cols, 4, shift),
            Area = SliceField(line, cols, 5, shift),
            Distance = SliceField(line, cols, 6, shift),
            OldHp = SliceField(line, cols, 7, shift),
            NewHp = SliceField(line, cols, 8, shift),
            Info = SliceField(line, cols, 9, shift)
        };

        private static string SliceField(string line, int[] cols, int index, int shift)
        {
            if (index >= cols.Length) return "";
            var start = cols[index] - shift;
            if (start < 0) start = 0;
            if (start >= line.Length) return "";
            var end = index + 1 < cols.Length ? cols[index + 1] - shift : line.Length;
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
