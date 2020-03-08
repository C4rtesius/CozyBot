using System;
using System.IO;
using System.Linq;
using System.Timers;
using System.Xml.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Globalization;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

using Discord;
using Discord.WebSocket;

using SixLabors.ImageSharp;
using SixLabors.Primitives;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace CozyBot
{
    public class PxlsAlertsModule : IGuildModule, IDisposable
    {
        private static ulong _pxlsOfficerId = 455305432312053760u;

        private static string _stringID => "PxlsAlertsModule";
        private static string _moduleXmlName => "pxls-alerts";
        private static string _moduleFolder => @"pxls-alerts";

        private static string _configFileName = "PxlsAlertsModuleConfig.json";

        private string _moduleConfigFilePath => Path.Combine(_guildPath, _configFileName);

        private PxlsAlertsModuleConfig _moduleConfig;

        private SocketGuild _guild;

        private Timer _alertTimer;
        private Timer _statsTimer;
        private static int _defaultTimeSpan = 120;

        protected XElement _configEl;
        protected List<ulong> _adminIds;
        protected bool _isActive;
        protected List<IBotCommand> _useCommands = new List<IBotCommand>();
        protected event ConfigChanged _configChanged;
        protected static string _defaultPrefix = "p!";
        protected string _guildPath;
        protected string _prefix;


        static string _infoDataUrl = @"https://pxls.space/info";
        static string _boardDataUrl = @"https://pxls.space/boarddata";
        static string _templateRegexPattern = @"template=(?<uri>https?[\:\w\d%\.\/]+(?:%2F|\/)(?<file>[\w\d]+))";
        static string _offsetxRegexPattern = @"ox=([0-9]+)\b";
        static string _offsetyRegexPattern = @"oy=([0-9]+)\b";

        static HttpClient _hc = new HttpClient();

        private string _modulePath => Path.Combine(_guildPath, _moduleFolder);

        public string StringID => _stringID;
        public SocketGuild Guild => _guild;
        public bool IsActive => _isActive;
        public string ModuleXmlName => _moduleXmlName;

        public event ConfigChanged GuildBotConfigChanged
        {
            add
            {
                if (value != null)
                {
                    _configChanged += value;
                }
            }
            remove
            {
                if (value != null)
                {
                    _configChanged -= value;
                }
            }
        }

        public IEnumerable<IBotCommand> ActiveCommands
        {
            get
            {
                foreach (var cmd in _useCommands)
                    yield return cmd;
            }
        }

        public PxlsAlertsModule(XElement configEl, List<ulong> adminIds, SocketGuild guild, string guildPath)
        {
            _guild = Guard.NonNull(guild, nameof(guild));
            _configEl = Guard.NonNull(configEl, nameof(configEl));
            _guildPath = Guard.NonNullWhitespaceEmpty(guildPath, nameof(guildPath));
            _adminIds = adminIds;
            _alertTimer = new Timer();

            if (_configEl.Element(ModuleXmlName) == null)
            {
                XElement moduleConfigEl =
                    new XElement(ModuleXmlName,
                        new XAttribute("on", Boolean.FalseString),
                        new XAttribute("prefix", _defaultPrefix)
                    );

                _configEl.Add(moduleConfigEl);
            }

            if (!Directory.Exists(_guildPath))
            {
                Directory.CreateDirectory(_guildPath);
            }

            if (!Directory.Exists(_modulePath))
                Directory.CreateDirectory(_modulePath);

            Configure(_configEl);
        }

        protected void Configure(XElement configEl)
        {
            XElement guildBotModuleCfg = configEl.Element(ModuleXmlName);

            bool isActive = false;

            if (guildBotModuleCfg.Attribute("on") != null)
            {
                if (!Boolean.TryParse(guildBotModuleCfg.Attribute("on").Value, out isActive))
                {
                    isActive = false;
                }
            }

            _isActive = isActive;

            string prefix = _defaultPrefix;

            if (guildBotModuleCfg.Attribute("prefix") != null)
            {
                if (!String.IsNullOrWhiteSpace(guildBotModuleCfg.Attribute("prefix").Value))
                {
                    prefix = guildBotModuleCfg.Attribute("prefix").Value;
                }
            }

            _prefix = prefix;

            if (!File.Exists(_moduleConfigFilePath))
            {
                CreateDefaultConfig();
            }

            _moduleConfig = LoadConfig(_moduleConfigFilePath);

            if (_isActive)
                GenerateUseCommands();
            else
                _useCommands = new List<IBotCommand>();
            if (_moduleConfig.Alerts == null)
            {
                _moduleConfig.Alerts = new Alerts() { Interval = _defaultTimeSpan };
#if DEBUG
                Console.WriteLine($"[DEBUG][PXLS] _moduleConfig.Alerts is NULL");
            }
            else
            {
                Console.WriteLine($"[DEBUG][PXLS] _moduleConfig.Alerts is NOT NULL");
            }
            {
                Console.WriteLine($"[DEBUG][PXLS] Interval value: {_moduleConfig.Alerts.Interval}");
                Console.WriteLine($"[DEBUG][PXLS] ChannelId value: {_moduleConfig.Alerts.ChannelId}");
                Console.WriteLine($"[DEBUG][PXLS] TemplateIds Any(): {_moduleConfig.Alerts.TemplateIds.Any()}");
#endif
            }
            _alertTimer.Interval = _moduleConfig.Alerts.Interval * 60000;
            _alertTimer.Elapsed += AlertTimerElapsed;
            _alertTimer.AutoReset = true;
            _alertTimer.Start();
        }

        private PxlsAlertsModuleConfig LoadConfig(string filePath)
        { 
            try
            {
                var data = File.ReadAllBytes(filePath);
                return JsonSerializer.Deserialize<PxlsAlertsModuleConfig>(data);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EXCEPT][PXLS] Catched :{ex.Message}");
                Console.WriteLine($"[EXCEPT][PXLS] Stack   :{ex.StackTrace}");
                throw;
            }
        }

        private void CreateDefaultConfig()
        {
            var cfg = new PxlsAlertsModuleConfig() { Palettes = new List<Palette>(), Templates = new List<Template>(), Alerts = new Alerts(), Stats = new Stats()};
            var o = new JsonSerializerOptions() { WriteIndented = true };
            var data = JsonSerializer.SerializeToUtf8Bytes<PxlsAlertsModuleConfig>(cfg, o);
            try
            {
                File.WriteAllBytes(_moduleConfigFilePath, data);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EXCEPT][PXLS] Catched :{ex.Message}");
                Console.WriteLine($"[EXCEPT][PXLS] Stack   :{ex.StackTrace}");
                throw;
            }
        }
        

        public void Reconfigure(XElement configEl)
        {
            if (configEl == null)
                return;
            Configure(configEl);
        }

        private void GenerateUseCommands()
        {
            var list = new List<IBotCommand>();

            list.Add(
                new BotCommand(
                    StringID + "-status",
                    RuleGenerator.PrefixatedCommand(_prefix, "status"),
                    StatusCmd
                )
            );

            list.Add(
                new BotCommand(
                    StringID + "-palette",
                    RuleGenerator.PrefixatedCommand(_prefix, "palette"),
                    PaletteCmd
                )
            );

            list.Add(
                new BotCommand(
                    StringID + "-template",
                    RuleGenerator.PrefixatedCommand(_prefix, "template"),
                    TemplateCmd
                )
            );

            list.Add(
                new BotCommand(
                    StringID + "-help",
                    RuleGenerator.PrefixatedCommand(_prefix, "help"),
                    HelpCmd
                )
            );

            list.Add(
                new BotCommand(
                    StringID + "-alert",
                    RuleGenerator.PrefixatedCommand(_prefix, "alert"),
                    AlertCmd
                )
            );

            list.Add(
                new BotCommand(
                    StringID + "-stats",
                    RuleGenerator.PrefixatedCommand(_prefix, "stats"),
                    StatsCmd
                )
            );

            //list.Add(
            //    new BotCommand(
            //        StringID + "-detemplatize",
            //        RuleGenerator.PrefixatedCommand(_prefix, "detempl"),
            //        DetemplatizeCmd
            //    )
            //);

            _useCommands = list;
        }

        private async Task StatsCmd(SocketMessage msg)
        {
            try
            {
#if DEBUG
                Console.WriteLine("[DEBUG][PXLS] Entered StatsCmd.");
#endif
                var words = msg.Content.Split(' ');
                if (words.Length == 1)
                {
                    await StatsUsrCmd(msg);
                    return;
                }
                var r = (msg.Author as SocketGuildUser).Roles.Select(r => r.Id);

                switch (words[1])
                {
                    case "g":
                    case "gl":
                    case "glob":
                    case "global":
                        await StatsGlobalCmd(msg).ConfigureAwait(false);
                        break;
                    case "c":
                    case "can":
                    case "canv":
                    case "canvas":
                        await StatsCanvasCmd(msg).ConfigureAwait(false);
                        break;
                    case "cfg":
                    case "conf":
                    case "config":
                        if (!r.Contains(_pxlsOfficerId))
                            return;
                        if (words.Length > 2)
                            await StatsCfgCmd(msg).ConfigureAwait(false);
                        break;
                    case "m":
                    case "me":
                        await StatsUsrCmd(msg).ConfigureAwait(false);
                        return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EXCEPT][PXLS] {ex.Message}");
                Console.WriteLine($"[EXCEPT][PXLS] {ex.StackTrace}");
                throw;
            }

        }

        private async Task StatsUsrCmd(SocketMessage msg)
        {
#if DEBUG
            Console.WriteLine("[DEBUG][PXLS] Entered StatsUsrCmd.");
#endif
            var q = _moduleConfig.Stats.Users.Where(u => u.UserId == msg.Author.Id);
            if (!q.Any())
            {
                await msg.Channel.SendMessageAsync($"{msg.Author.Mention} вашого айді не знайдено. Зверніться до піксельного офіцера.").ConfigureAwait(false);
            }

            var userName = q.First().CanvasName;

            //var words = msg.Content.Split(' ');
            if (_moduleConfig.Stats.LastDownload < DateTime.UtcNow - TimeSpan.FromMinutes(15.0d))
            {
                await DownloadStatsJson().ConfigureAwait(false);
            }
            string statsJsonPath = Path.Combine(_modulePath, "stats.json");
            StatsJson stats = null;
            using (var fs = new FileStream(statsJsonPath, FileMode.Open, FileAccess.ReadWrite))
            {
                try
                {
                    stats = await JsonSerializer.DeserializeAsync<StatsJson>(fs);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EXCEPT][PXLS] {ex.Message}");
                    Console.WriteLine($"[EXCEPT][PXLS] {ex.StackTrace}");
                    throw;
                }
            }
            var qg = stats.TopList.Alltime.Where(u => u.Username == userName);
            var qc = stats.TopList.Canvas.Where(u => u.Username == userName);

            string output = $"**Статистика для {msg.Author.Mention} :{Environment.NewLine}**";
            output += $"`{"За весь час",-20}";
            output += @$":{(qg.Any() ? $" Місце: {qg.First().Place,5} Пікселів: {qg.First().Pixels,7}" : "Замало пікселів для статистики. Ставте більше.")}`{Environment.NewLine}";
            output += $"`{"За цей канвас",-20}";
            output += @$":{(qc.Any() ? $" Місце: {qc.First().Place,5} Пікселів: {qc.First().Pixels,7}" : "Замало пікселів для статистики. Ставте більше.")}`{Environment.NewLine}";

            await msg.Channel.SendMessageAsync(output).ConfigureAwait(false);
        }


        private async Task StatsGlobalCmd(SocketMessage msg)
        {
#if DEBUG
            Console.WriteLine("[DEBUG][PXLS] Entered StatsGlobalCmd.");
#endif
            //var words = msg.Content.Split(' ');
            if (_moduleConfig.Stats.LastDownload < DateTime.UtcNow - TimeSpan.FromMinutes(15.0d))
            {
                await DownloadStatsJson().ConfigureAwait(false);
            }
            string statsJsonPath = Path.Combine(_modulePath, "stats.json");
            StatsJson stats = null;
            using (var fs = new FileStream(statsJsonPath, FileMode.Open, FileAccess.ReadWrite))
            {
                try
                {
                    stats = await JsonSerializer.DeserializeAsync<StatsJson>(fs);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EXCEPT][PXLS] {ex.Message}");
                    Console.WriteLine($"[EXCEPT][PXLS] {ex.StackTrace}");
                    throw;
                }
            }
            var usersStrs = _moduleConfig.Stats.Users.Select(u => u.CanvasName);
            var q = stats.TopList.Alltime.Where(u => usersStrs.Contains(u.Username));
            foreach (var us in q)
            {
                var um = _moduleConfig.Stats.Users.First(um => um.CanvasName == us.Username);
                um.GlobalCount = us.Pixels;
                um.GlobalPlace = us.Place;
            }

            await SaveConfig().ConfigureAwait(false);

            var users = new List<User>(_moduleConfig.Stats.Users);
            /*users.OrderBy(u => u.GlobalPlace)*/;
            var s = '\u2502';
            string output = $"`{"Place",5}{s}{"Pixels",7}{s}{"Canvas Username",20} {s} {"Server name"}`{Environment.NewLine}";
            var outputStrs = new List<string>();
            
            users.FindAll(u => u.GlobalPlace == 0).ForEach(u => u.GlobalPlace = 1001);
            foreach(var user in users.OrderBy(u => u.GlobalPlace))
            {
                var uname = _guild.GetUser(user.UserId).Nickname ?? _guild.GetUser(user.UserId).Username;
                var str = $"`{user.GlobalPlace,5}{s}{user.GlobalCount,7}{s}{user.CanvasName,20} {s} {uname}`{Environment.NewLine}";
                if (str.Length + output.Length > 1000)
                {
                    outputStrs.Add(output);
                    output = str;
                }
                else
                {
                    output += str;
                }
            }
            outputStrs.Add(output);

            string iconUrl = _guild.IconUrl;
            var eba = new EmbedAuthorBuilder
            {
                Name = @"Shining Armor"
                //, IconUrl = @"https://cdn.discordapp.com/avatars/335004246007218188/3094a7be163d3cd1d03278b53c8f08eb.png"
            };



            var efob = new EmbedFooterBuilder
            {
                Text = "Мы в котле были в первую пиксельную."
            };

            var eb = new EmbedBuilder
            {
                Author = eba,
                Color = Discord.Color.Green,
                ThumbnailUrl = iconUrl,
                Title = "Топ за весь час :",
                Timestamp = DateTime.Now,
                Footer = efob
            };
            
            foreach (var str in outputStrs)
            {
                var efb = new EmbedFieldBuilder
                {
                    IsInline = false,
                    Name = "Топ Укриччан :",
                    Value = str
                };
                eb.Fields.Add(efb);
            }

            //var dm = await msg.Author.GetOrCreateDMChannelAsync();

            await msg.Channel.SendMessageAsync(String.Empty, false, eb.Build()).ConfigureAwait(false);
        }

        private async Task StatsCanvasCmd(SocketMessage msg)
        {
#if DEBUG
            Console.WriteLine("[DEBUG][PXLS] Entered StatsCanvasCmd.");
#endif
            //var words = msg.Content.Split(' ');
            if (_moduleConfig.Stats.LastDownload < DateTime.UtcNow - TimeSpan.FromMinutes(15.0d))
            {
                await DownloadStatsJson().ConfigureAwait(false);
            }
            string statsJsonPath = Path.Combine(_modulePath, "stats.json");
            StatsJson stats = null;
            using (var fs = new FileStream(statsJsonPath, FileMode.Open, FileAccess.ReadWrite))
            {
                try
                {
                    stats = await JsonSerializer.DeserializeAsync<StatsJson>(fs);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EXCEPT][PXLS] {ex.Message}");
                    Console.WriteLine($"[EXCEPT][PXLS] {ex.StackTrace}");
                    throw;
                }
            }
            var usersStrs = _moduleConfig.Stats.Users.Select(u => u.CanvasName);
            var q = stats.TopList.Canvas.Where(u => usersStrs.Contains(u.Username));
            foreach (var us in q)
            {
                var um = _moduleConfig.Stats.Users.First(um => um.CanvasName == us.Username);
                um.CanvasCount = us.Pixels;
                um.CanvasPlace = us.Place;
            }

            await SaveConfig().ConfigureAwait(false);

            var users = new List<User>(_moduleConfig.Stats.Users);
            //users.OrderByDescending(u => u.CanvasPlace);
            var s = '\u2502';
            string output = $"`{"Place",5}{s}{"Pixels",7}{s}{"Canvas Username",20} {s} {"Server name"}`{Environment.NewLine}";
            var outputStrs = new List<string>();

            users.FindAll(u => u.CanvasPlace == 0).ForEach(u => u.CanvasPlace = 1001);
            foreach (var user in users.OrderBy(u => u.CanvasPlace))
            {
                var uname = _guild.GetUser(user.UserId).Nickname ?? _guild.GetUser(user.UserId).Username;
                var str = $"`{user.CanvasPlace,5}{s}{user.CanvasCount,7}{s}{user.CanvasName,20} {s} {uname}`{Environment.NewLine}";
                if (str.Length + output.Length > 1000)
                {
                    outputStrs.Add(output);
                    output = str;
                }
                else
                {
                    output += str;
                }
            }
            outputStrs.Add(output);

            string iconUrl = _guild.IconUrl;
            var eba = new EmbedAuthorBuilder
            {
                Name = @"Shining Armor"
                //, IconUrl = @"https://cdn.discordapp.com/avatars/335004246007218188/3094a7be163d3cd1d03278b53c8f08eb.png"
            };

            var efob = new EmbedFooterBuilder
            {
                Text = "Мы в котле были в первую пиксельную."
            };

            var eb = new EmbedBuilder
            {
                Author = eba,
                Color = Discord.Color.Green,
                ThumbnailUrl = iconUrl,
                Title = "Топ канвасу :",
                Timestamp = DateTime.Now,
                Footer = efob
            };

            foreach (var str in outputStrs)
            {
                var efb = new EmbedFieldBuilder
                {
                    IsInline = false,
                    Name = "Топ Укриччан :",
                    Value = str
                };
                eb.Fields.Add(efb);
            }

            //var dm = await msg.Author.GetOrCreateDMChannelAsync();

            await msg.Channel.SendMessageAsync(String.Empty, false, eb.Build()).ConfigureAwait(false);
        }

        private async Task DownloadStatsJson()
        {
            var uri = @"https://pxls.space/stats/stats.json";

            //File.WriteAllText("stats.json", data);
            //var stats = JsonSerializer.Deserialize<StatsJson>(data);
            //foreach (var user in stats.TopList.Canvas)
            //{
            //    Console.WriteLine($"{user.Username} {user.Pixels} {user.Place}");
            //}

            string statsJsonPath = Path.Combine(_modulePath, "stats.json");

            try
            {
                using var response = await _hc.GetAsync(Uri.UnescapeDataString($"{uri}")).ConfigureAwait(false);
                using var cs = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using (var fs = new FileStream(statsJsonPath, FileMode.Create, FileAccess.ReadWrite))
                    await cs.CopyToAsync(fs).ConfigureAwait(false);

                _moduleConfig.Stats.LastDownload = DateTime.UtcNow;
                
                await SaveConfig().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EXCEPT][PXLS] {ex.Message}");
                Console.WriteLine($"[EXCEPT][PXLS] {ex.StackTrace}");
                throw;
            }
        }

        private async Task StatsCfgCmd(SocketMessage msg)
        {
#if DEBUG
            Console.WriteLine("[DEBUG][PXLS] Entered StatsCfgCmd.");
#endif
            var words = msg.Content.Split(' ');
            Regex userRegex = new Regex(@"^user:(?<userId>[0-9]+):(?<canvasName>[\w_-]+)");

            foreach (var word in words)
            {
                if (word.StartsWith("interval:"))
                {
                    var intStr = word.Replace("interval:", String.Empty);
                    if (Int32.TryParse(intStr, out int interval))
                        _moduleConfig.Stats.Interval = interval;
                    continue;
                }
                if (word.StartsWith("auto:"))
                {
                    var autoStr = word.Replace("auto:", String.Empty);
                    if (Boolean.TryParse(autoStr, out bool auto))
                        _moduleConfig.Stats.Auto = auto;
                    continue;
                }
                if (word.StartsWith("channel:"))
                {
                    var channelStr = word.Replace("channel:", String.Empty);
                    if (UInt64.TryParse(channelStr, out ulong channelId))
                        _moduleConfig.Stats.ChannelId = channelId;
                    continue;
                }
                if (word.StartsWith("deluser:"))
                {
                    var userStr = word.Replace("deluser:", String.Empty);
                    IEnumerable<User> q;
                    if (UInt64.TryParse(userStr, out ulong userId))
                    {
                        q = _moduleConfig.Stats.Users.Where(u => u.UserId == userId);
                        if (q.Any())
                        {
                            var u = q.First();
                            _moduleConfig.Stats.Users.Remove(u);
                            continue;
                        }
                    }
                    q = _moduleConfig.Stats.Users.Where(u => u.CanvasName == userStr);
                    if (q.Any())
                    {
                        var u = q.First();
                        _moduleConfig.Stats.Users.Remove(u);
                        continue;
                    }
                }
                if (userRegex.IsMatch(word))
                {
                    var userStr = userRegex.Match(word).Groups["userId"].Value;
                    var canvasName = userRegex.Match(word).Groups["canvasName"].Value;
                    if (UInt64.TryParse(userStr, out ulong userId))
                    {
                        _moduleConfig.Stats.Users.Add(new User() { UserId = userId, CanvasName = canvasName });
                    }
                    else
                        continue;
                }
            }

            await SaveConfig().ConfigureAwait(false);
        }

        private async Task AlertCmd(SocketMessage msg)
        {
            var words = msg.Content.Split(' ');
            if (words.Length == 1)
                return;
            var r = (msg.Author as SocketGuildUser).Roles.Select(r => r.Id);
            if (!r.Contains(_pxlsOfficerId))
                return;
            switch (words[1]) 
            {
                case "on" when words.Length > 2:
                    await AlertOnCmd(msg).ConfigureAwait(false);
                    return;
                case "off" when words.Length == 3:
                    await AlertOffCmd(msg).ConfigureAwait(false);
                    return;
                case "cfg" when words.Length > 2:
                    await AlertCfgCmd(msg).ConfigureAwait(false);
                    return;
            }
        }

        private async Task AlertCfgCmd(SocketMessage msg)
        {
            var words = msg.Content.Split(' ');
            foreach(var word in words)
            {
                if (word.StartsWith("interval:"))
                {
                    var intStr = word.Replace("interval:", String.Empty);
                    if (Int32.TryParse(intStr, out int interval))
                        _moduleConfig.Alerts.Interval = interval;
                    else
                        continue;
                    _alertTimer.Stop();
                    _alertTimer.Interval = interval * 60 * 1000;
                    _alertTimer.Start();
                }
                if (word.StartsWith("channel:"))
                {
                    var intStr = word.Replace("channel:", String.Empty);
                    if (UInt64.TryParse(intStr, out ulong channel))
                        _moduleConfig.Alerts.ChannelId = channel;
                    else
                        continue;
                }
            }
            // TODO : add channel permissions check
            await SaveConfig().ConfigureAwait(false);
        }

        private async Task AlertOnCmd(SocketMessage msg)
        {
#if DEBUG
            Console.WriteLine($"[DEBUG][PXLS] Entered AlertOnCmd.");
#endif
            var words = msg.Content.Split(' ');
            var tName = words[2];
            var q = _moduleConfig.Templates.Where(t => t.Name == tName);
            if (!q.Any())
                return;
            var id = q.First().Id;
            if (_moduleConfig.Alerts.TemplateIds.Contains(id))
                return;
#if DEBUG
            Console.WriteLine($"[DEBUG][PXLS] Got id {id}");
#endif

            var ch = _guild.GetChannel(_moduleConfig.Alerts.ChannelId) as ISocketMessageChannel;
#if DEBUG
            Console.WriteLine($"[DEBUG][PXLS] Got channel {ch.Id}");
            Console.WriteLine($"[DEBUG][PXLS] Got interval {_moduleConfig.Alerts.Interval}");
#endif

            await GetStatus(q.First().Url, ch, false).ConfigureAwait(false);
#if DEBUG
            Console.WriteLine($"[DEBUG][PXLS] GetStatusFinished.");
#endif

            _moduleConfig.Alerts.TemplateIds.Add(id);

            await SaveConfig().ConfigureAwait(false);
#if DEBUG
            Console.WriteLine($"[DEBUG][PXLS] Config Saved.");
#endif
        }

        private async Task AlertOffCmd(SocketMessage msg)
        {
            var words = msg.Content.Split(' ');
            var tName = words[2];
            var q = _moduleConfig.Templates.Where(t => t.Name == tName);
            if (!q.Any())
                return;
            var id = q.First().Id;
            if (!_moduleConfig.Alerts.TemplateIds.Contains(id))
                return;
            _moduleConfig.Alerts.TemplateIds.Remove(id);
            
            await SaveConfig().ConfigureAwait(false);
        }

        private async void AlertTimerElapsed(object o, ElapsedEventArgs e)
        {
            List<int> removeList = new List<int>();
            var ch = _guild.GetChannel(_moduleConfig.Alerts.ChannelId) as ISocketMessageChannel;

            foreach (var id in _moduleConfig.Alerts.TemplateIds)
            {
                var q = _moduleConfig.Templates.Where(t => t.Id == id);
                if (!q.Any())
                {
                    removeList.Add(id);
                    continue;
                }
                await GetStatus(q.First().Url, ch, false).ConfigureAwait(false);
            }
            foreach (var id in removeList)
                _moduleConfig.Alerts.TemplateIds.Remove(id);
        }

        private async Task GetStatus(string inputUri, ISocketMessageChannel sc, bool cmd)
        {
            if (sc == null)
                return;

            if (!inputUri.StartsWith(@"https://pxls.space/#"))
            {
                var q = _moduleConfig.Templates.Where(t => t.Name == inputUri);
                if (!q.Any())
                    return;
                inputUri = q.First().Url;
            }

            var templateRegex = new Regex(_templateRegexPattern, RegexOptions.Compiled);
            var offsetxRegex = new Regex(_offsetxRegexPattern, RegexOptions.Compiled);
            var offsetyRegex = new Regex(_offsetyRegexPattern, RegexOptions.Compiled);

            var templateMatch = templateRegex.Match(inputUri);
            var offsetxString = offsetxRegex.Match(inputUri).Groups[1].Value;
            var offsetyString = offsetyRegex.Match(inputUri).Groups[1].Value;

            if (!Uri.TryCreate(templateMatch.Groups["uri"].Value, UriKind.RelativeOrAbsolute, out Uri uri))
#if DEBUG
            {
                Console.WriteLine($"[DEBUG][PXLS-ALERTS] Invalid uri :{templateMatch.Groups["uri"].Value}");
                return;
            }
#else
                return;
#endif
            var output = templateMatch.Groups["file"].Value;

            if (String.IsNullOrEmpty(output) || String.IsNullOrWhiteSpace(output))
#if DEBUG
            {
                Console.WriteLine($"[DEBUG][PXLS-ALERTS] Invalid output :{output}");
                return;
            }
#else
                return;
#endif
            if (!Int32.TryParse(offsetxString, out int offsetx))
#if DEBUG
            {
                Console.WriteLine($"[DEBUG][PXLS-ALERTS] Invalid offsetx :{offsetxString}");
                return;
            }
#else
                return;
#endif
            if (!Int32.TryParse(offsetyString, out int offsety))
#if DEBUG
            {
                Console.WriteLine($"[DEBUG][PXLS-ALERTS] Invalid offsety :{offsetxString}");
                return;
            }
#else
                return;
#endif

#if DEBUG
            Console.WriteLine($"[DEBUG][PXLS-ALERTS] Checks passed.");
#endif
            string rawTemplatePath = Path.Combine(_modulePath, output);

            try
            {
                using var response = await _hc.GetAsync(Uri.UnescapeDataString($"{uri}")).ConfigureAwait(false);
                using var cs = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using (var fs = new FileStream(rawTemplatePath, FileMode.Create, FileAccess.ReadWrite))
                    await cs.CopyToAsync(fs).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                return;
            }

#if DEBUG
            Console.WriteLine($"[DEBUG][PXLS-ALERTS] Template downloaded.");
#endif
            SixLabors.ImageSharp.Image<Rgba32> template;
            try
            {
                template = SixLabors.ImageSharp.Image.Load(Path.Combine(_modulePath, output)).CloneAs<Rgba32>();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                return;
            }

            CanvasData canvasData;

            try
            {
                canvasData = await GetCanvasData(_infoDataUrl, _boardDataUrl);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                return;
            }
#if DEBUG
            Console.WriteLine($"[DEBUG][PXLS-ALERTS] Canvas data downloaded.");
#endif

            int symbolSize = GetTemplateSymbolSizeFromMetadata(template);
            if (symbolSize == 0)
                symbolSize = GetTemplateSymbolSize(template, canvasData.Palette.Values);
            else
                template[0, 0] = new Rgba32(template[0, 0].R, template[0, 0].G, template[0, 0].B, (template[0, 0].A > 128) ? 255 : 0);

            using var img = Detemplatize(template, symbolSize, canvasData.Palette.Values);

            string pngImagePath = Path.Combine(_modulePath, $"{output}.png");
            try
            {
                using (var fs = new FileStream(pngImagePath, FileMode.Create, FileAccess.ReadWrite))
                    img.SaveAsPng(fs);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                return;
            }

#if DEBUG
            Console.WriteLine($"[DEBUG][PXLS-ALERTS] Image detemplatized.");
#endif

            int totalPxls = 0;
            int wrongPxls = 0;

            var transparent = new Rgba32(0, 0, 0, 0);

            int xLow;
            int xHigh;
            int yLow;
            int yHigh;

            if (offsetx < 0)
            {
                xLow = -offsetx;
                if (offsetx + img.Width < 0)
                    return;
                else if (offsetx + img.Width >= canvasData.Width)
                    xHigh = img.Width - canvasData.Width + offsetx;
                else
                    xHigh = img.Width;
            }
            else if (offsetx >= canvasData.Width)
            {
                return;
            }
            else
            {
                xLow = 0;
                if (offsetx + img.Width < canvasData.Width)
                    xHigh = img.Width;
                else
                    xHigh = canvasData.Width - offsetx;
            }

            if (offsety < 0)
            {
                yLow = -offsety;
                if (offsety + img.Height < 0)
                    return;
                else if (offsety + img.Height >= canvasData.Height)
                    yHigh = img.Height - canvasData.Height + offsety;
                else
                    yHigh = img.Height;
            }
            else if (offsety >= canvasData.Height)
                return;
            else
            {
                yLow = 0;
                if (offsety + img.Height < canvasData.Height)
                    yHigh = img.Height;
                else
                    yHigh = canvasData.Height - offsety;
            }

            using var wrongMap = new Image<Rgba32>(Configuration.Default, xHigh - xLow, yHigh - yLow, Rgba32.Transparent);

            for (int x = 0; x < xHigh - xLow; x++)
            {
                for (int y = 0; y < yHigh - yLow; y++)
                {
                    if (img[x + xLow, y + yLow] != transparent)
                    {
                        totalPxls++;
                        if (canvasData[offsetx + x + xLow, offsety + y + yLow] != img[x + xLow, y + yLow])
                        {
                            wrongPxls++;
                            wrongMap[x, y] = Rgba32.Red;
                        }
                    }
                }
            }

            if (!cmd && wrongPxls == 0)
                return;

#if DEBUG
            Console.WriteLine($"[DEBUG][PXLS-ALERTS] Wrongmap formed.");
#endif

            float amountC = 0.7f;
            float amountT = 0.7f;
            int border = 20;
            using var res = canvasData.Canvas.Clone();

            if (offsetx < 0)
            {
                xLow = 0;
                if (wrongMap.Width + border >= canvasData.Width)
                    xHigh = canvasData.Width;
                else
                    xHigh = wrongMap.Width + border;
            }
            else if (offsetx - border < 0)
            {
                xLow = 0;
                if (wrongMap.Width + border + offsetx >= canvasData.Width)
                    xHigh = canvasData.Width;
                else
                    xHigh = wrongMap.Width + border + offsetx;
            }
            else
            {
                xLow = offsetx - border;
                if (wrongMap.Width + border + offsetx >= canvasData.Width)
                    xHigh = border - offsetx + canvasData.Width;
                else
                    xHigh = 2 * border + wrongMap.Width;
            }

            if (offsety < 0)
            {
                yLow = 0;
                if (wrongMap.Height + border >= canvasData.Height)
                    yHigh = canvasData.Height;
                else
                    yHigh = wrongMap.Height + border;
            }
            else if (offsety - border < 0)
            {
                yLow = 0;
                if (wrongMap.Height + border + offsety >= canvasData.Height)
                    yHigh = canvasData.Height;
                else
                    yHigh = wrongMap.Height + border + offsety;
            }
            else
            {
                yLow = offsety - border;
                if (wrongMap.Height + border + offsety >= canvasData.Height)
                    yHigh = border - offsety + canvasData.Height;
                else
                    yHigh = 2 * border + wrongMap.Height;
            }

            var newSize = (xHigh > yHigh) ? new Size(800, (int)(yHigh * 800f / xHigh)) : new Size((int)(xHigh * 800f / yHigh), 800);
            var resampler = (xHigh > yHigh) ? ((xHigh > 800) ? KnownResamplers.Bicubic : KnownResamplers.NearestNeighbor) :
                (yHigh > 800) ? KnownResamplers.Bicubic : KnownResamplers.NearestNeighbor;

            GraphicsOptions go = new GraphicsOptions { ColorBlendingMode = PixelColorBlendingMode.Subtract, BlendPercentage = amountT };
            ResizeOptions ro = new ResizeOptions { Mode = ResizeMode.Stretch, Position = AnchorPositionMode.Center, Sampler = resampler, Size = newSize };

            res.Mutate(
                o =>
                    o.Brightness(amountC)
                    .DrawImage(img, new Point(offsetx, offsety), go)
                    .DrawImage(wrongMap, new Point((offsetx < 0) ? 0 : offsetx, (offsety < 0) ? 0 : offsety), 1.0f)
                    .Crop(new Rectangle(xLow, yLow, xHigh, yHigh))
                    .Resize(ro)
            );

#if DEBUG
            Console.WriteLine($"[DEBUG][PXLS-ALERTS] Wrongmap image processed and formed.");
#endif
            string wrongMapFilePath = Path.Combine(_modulePath, $"{output}_wrongmap.png");

            try
            {
                using (var fs = new FileStream(wrongMapFilePath, FileMode.Create, FileAccess.ReadWrite))
                    res.SaveAsPng(fs);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                return;
            }

#if DEBUG
            Console.WriteLine($"[DEBUG][PXLS-ALERTS] Wrongmap image saved.");
#endif
            try
            {
                string outMsg = $"```{wrongPxls} incorrect from {totalPxls} total.{Environment.NewLine}Percent done : {100 - 100f * wrongPxls / totalPxls}```";
                await sc.SendMessageAsync(outMsg).ConfigureAwait(false);
                await sc.SendFileAsync(wrongMapFilePath).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                return;
            }
#if DEBUG
            Console.WriteLine($"[DEBUG][PXLS-ALERTS] Message and image sent.");
#endif
            try
            {
                File.Delete(rawTemplatePath);
                File.Delete(pngImagePath);
                File.Delete(wrongMapFilePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                return;
            }
#if DEBUG
            Console.WriteLine($"[DEBUG][PXLS-ALERTS] Temp files deleted.");
#endif
        }

        private async Task HelpCmd(SocketMessage msg)
        {
            Guard.NonNull(msg, nameof(msg));
            if (msg.Author is SocketGuildUser user)
            {
                try
                {
                    await msg.DeleteAsync().ConfigureAwait(false);
                }
                catch
                {

                }

                var guild = user.Guild;
                string iconUrl = guild.IconUrl;
                var eba = new EmbedAuthorBuilder
                {
                    Name = @"Shining Armor"
                    //, IconUrl = @"https://cdn.discordapp.com/avatars/335004246007218188/3094a7be163d3cd1d03278b53c8f08eb.png"
                };

                var efb = new EmbedFieldBuilder
                {
                    IsInline = false,
                    Name = "Команди піксельного модуля",
                    Value =
                        "`" + _prefix + @"status [посилання на шаблон|назва шаблону]` - стан виконання шаблону за посиланням" + Environment.NewLine +
                        "`" + _prefix + @"palette list` - список палітр" + Environment.NewLine +
                        "`" + _prefix + @"palette add [назва палітри] [файл палітри]` - додати палітру" + Environment.NewLine +
                        "`" + _prefix + @"palette get [назва палітри] ?[text|file]` - отримати палітру у вигляді тексту або файлу" + Environment.NewLine +
                        "`" + _prefix + @"palette del [назва палітри]` - видалити палітру" + Environment.NewLine +
                        "`" + _prefix + @"template list` - список шаблонів" + Environment.NewLine +
                        "`" + _prefix + @"template add [назва шаблону]` [посилання на шаблон] - додати шаблон" + Environment.NewLine +
                        "`" + _prefix + @"template del [назва шаблону]` - видалити шаблон" + Environment.NewLine +
                        "`" + _prefix + @"template get [назва шаблону]` - отримати шаблон та метадані" + Environment.NewLine +
                        "`" + _prefix + @"template detempl [пікча|посилання] ?[назва палітри]` - отримати зображення з шаблона" + Environment.NewLine +
                        "`" + _prefix + @"template make [пікча|посилання] ?dotted ?[назва палітри]` - отримати шаблон з зображення" + Environment.NewLine +
                        "`" + _prefix + @"stats ?[m|me]` - ваша статистика" + Environment.NewLine +
                        "`" + _prefix + @"stats [g|gl|glob|global]` - статистика за весь час" + Environment.NewLine +
                        "`" + _prefix + @"stats [c|can|canv|canvas]` - статистика за цей канвас" + Environment.NewLine +
                        " " + @"**Якщо вас нема в статистиці - звертайтесь до Піксельних Офіцерів.**" + Environment.NewLine +
                        "`" + _prefix + @"help` - цей список команд"
                };

                var efob = new EmbedFooterBuilder
                {
                    Text = "Мы в котле были в первую пиксельную."
                };

                var eb = new EmbedBuilder
                {
                    Author = eba,
                    Color = Discord.Color.Green,
                    ThumbnailUrl = iconUrl,
                    Title = "Довідка :",
                    Timestamp = DateTime.Now,
                    Footer = efob
                };

                eb.Fields.Add(efb);

                //var dm = await msg.Author.GetOrCreateDMChannelAsync();

                await msg.Channel.SendMessageAsync(String.Empty, false, eb.Build()).ConfigureAwait(false);

                //string output = msg.Author.Mention + " подивись в приватні повідомлення " + EmojiCodes.Bumagi;

                //await msg.Channel.SendMessageAsync(output).ConfigureAwait(false);


            }
        }

        private async Task DetemplatizeCmd(SocketMessage msg)
        {
            var words = msg.Content.Split(" ");
            if (msg.Attachments.Any())
            {
                switch (words.Length)
                {
                    case 2:
                        await DetemplByAttachmentCmd(msg).ConfigureAwait(false);
                        return;
                    case 3:
                        await DetemplByAttachmentAndPaletteCmd(msg).ConfigureAwait(false);
                        return;
                    default:
                        break;
                }
            }
            else if (words.Length > 2 && words.Length < 5)
            {
                switch (words.Length)
                {
                    case 3:
                        await DetemplByLinkCmd(msg).ConfigureAwait(false);
                        return;
                    case 4:
                        await DetemplByLinkAndPaletteCmd(msg).ConfigureAwait(false);
                        return;
                    default:
                        break;
                }
            }
            else
            {
                await msg.Channel.SendMessageAsync("Unrecognized command format.").ConfigureAwait(false);
            }
        }
        private async Task DetemplByAttachmentAndPaletteCmd(SocketMessage msg)
        {
            var url = msg.Attachments.First().Url;
            var file = await GetTemplateFilePathByUrl(url).ConfigureAwait(false);

            if (String.IsNullOrEmpty(file))
            {
                await msg.Channel.SendMessageAsync("Bad URL.").ConfigureAwait(false);
                return;
            }

            var paletteName = msg.Content.Split(" ")[2];
            var q = _moduleConfig.Palettes.Where(p => p.Name == paletteName);
            
            List<Rgba32> pd = new List<Rgba32>();

            if (!q.Any())
            {
                await msg.Channel.SendMessageAsync("Specified palette not found. Using current pxls.space palette.").ConfigureAwait(false);
                await DetemplByLinkCmd(msg).ConfigureAwait(false);
            }
            else
            {

                foreach (var c in q.First().Colours)
                    pd.Add(new Rgba32(c.Red, c.Green, c.Blue));
            }

            var image = await DetemplHelper(msg, file, pd).ConfigureAwait(false);
            if (String.IsNullOrEmpty(image))
            {
                await msg.Channel.SendMessageAsync("Detemplatization unsuccessful.").ConfigureAwait(false);
                return;
            }

            await msg.Channel.SendFileAsync(image).ConfigureAwait(false);

            File.Delete(file);
            File.Delete(image);
        }

        private async Task DetemplByAttachmentCmd(SocketMessage msg)
        {
            var url = msg.Attachments.First().Url;
            var file = await GetTemplateFilePathByUrl(url).ConfigureAwait(false);

            if (String.IsNullOrEmpty(file))
            {
                await msg.Channel.SendMessageAsync("Bad URL.").ConfigureAwait(false);
                return;
            }

            CanvasData canvasData;

            try
            {
                canvasData = await GetCanvasData(_infoDataUrl, _boardDataUrl).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                return;
            }

            var image = await DetemplHelper(msg, file, canvasData.Palette.Values).ConfigureAwait(false);
            if (String.IsNullOrEmpty(image))
            {
                await msg.Channel.SendMessageAsync("Detemplatization unsuccessful.").ConfigureAwait(false);
                return;
            }

            await msg.Channel.SendFileAsync(image).ConfigureAwait(false);

            File.Delete(file);
            File.Delete(image);
        }

        private async Task DetemplByLinkCmd(SocketMessage msg)
        {
            var url = msg.Content.Split(" ")[2];
            var file = await GetTemplateFilePathByUrl(url).ConfigureAwait(false);
            if (String.IsNullOrEmpty(file))
            {
                await msg.Channel.SendMessageAsync("Bad URL.").ConfigureAwait(false);
                return;
            }

            CanvasData canvasData;

            try
            {
                canvasData = await GetCanvasData(_infoDataUrl, _boardDataUrl).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                return;
            }

            var image = await DetemplHelper(msg, file, canvasData.Palette.Values).ConfigureAwait(false);
            if (String.IsNullOrEmpty(image))
            {
                await msg.Channel.SendMessageAsync("Detemplatization unsuccessful.").ConfigureAwait(false);
                return;
            }

            await msg.Channel.SendFileAsync(image).ConfigureAwait(false);

            File.Delete(file);
            File.Delete(image);
        }

        private async Task<string> DetemplHelper(SocketMessage msg, string file, IEnumerable<Rgba32> pd)
        {
            SixLabors.ImageSharp.Image<Rgba32> template;

            try
            {
                template = SixLabors.ImageSharp.Image.Load(file).CloneAs<Rgba32>();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                return String.Empty;
            }

            int symbolSize = GetTemplateSymbolSizeFromMetadata(template);
            if (symbolSize == 0)
                symbolSize = GetTemplateSymbolSize(template, pd);
            else
                template[0, 0] = new Rgba32(template[0, 0].R, template[0, 0].G, template[0, 0].B, (template[0, 0].A > 128) ? 255 : 0);

            using var img = Detemplatize(template, symbolSize, pd);

            string pngImagePath = $"{file}";//.png";
            if (!Path.HasExtension(file))
                pngImagePath += ".png";

            try
            {
                using (var fs = new FileStream(pngImagePath, FileMode.Create, FileAccess.ReadWrite))
                    img.SaveAsPng(fs);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                return String.Empty;
            }

            return pngImagePath;
        }

        private async Task<string> GetTemplateFilePathByUrl(string url)
        {
            string fileName = String.Empty;
            if (url.StartsWith(@"https://pxls.space/#"))
            {
                var templateRegex = new Regex(_templateRegexPattern);
                var templateMatch = templateRegex.Match(url);
                if (!Uri.TryCreate(templateMatch.Groups["uri"].Value, UriKind.RelativeOrAbsolute, out var _))
#if DEBUG
                {
                    Console.WriteLine($"[DEBUG][PXLS-ALERTS] Invalid uri :{templateMatch.Groups["uri"].Value}");
                    return String.Empty;
                }
#else
                    return String.Empty;
#endif
                url = Uri.UnescapeDataString(templateMatch.Groups["uri"].Value);
                fileName = templateMatch.Groups["file"].Value;
            }
            else
            {
                if (!Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var _))
#if DEBUG
                {
                    Console.WriteLine($"[DEBUG][PXLS-ALERTS] Invalid uri :{url}");
                    return String.Empty;
                }
#else
                    return String.Empty;
#endif
                url = Uri.UnescapeDataString(url);
                fileName = (new FileInfo(url)).Name;
            }

            //if (
            //    String.IsNullOrEmpty((new FileInfo(fileName)).Extension) ||
            //    String.IsNullOrWhiteSpace((new FileInfo(fileName)).Extension)
            //)
            //{
            //    var extension =
            //        (String.IsNullOrEmpty((new FileInfo(url)).Extension)
            //        || String.IsNullOrWhiteSpace((new FileInfo(url)).Extension))
            //        ? "png"
            //        : (new FileInfo(url)).Extension;
            //    fileName += "." + extension;
            //}

            fileName = Path.Combine(_modulePath, fileName);

            using var response = await _hc.GetAsync(Uri.UnescapeDataString($"{url}"));
            using var cs = await response.Content.ReadAsStreamAsync();
            using (var fs = new FileStream(fileName, FileMode.Create, FileAccess.ReadWrite))
                await cs.CopyToAsync(fs).ConfigureAwait(false);

            return fileName;
        }

        private async Task DetemplByLinkAndPaletteCmd(SocketMessage msg)
        {
            var paletteName = msg.Content.Split(" ")[2];
            var q = _moduleConfig.Palettes.Where(p => p.Name == paletteName);
            if (!q.Any())
            {
                await msg.Channel.SendMessageAsync("Specified palette not found. Using current pxls.space palette.").ConfigureAwait(false);
                await DetemplByLinkCmd(msg).ConfigureAwait(false);
                return;
            }

            List<Rgba32> pd = new List<Rgba32>();

            foreach (var c in q.First().Colours)
                pd.Add(new Rgba32(c.Red, c.Green, c.Blue));

            var url = msg.Content.Split(" ")[3];
            var file = await GetTemplateFilePathByUrl(url).ConfigureAwait(false);
            if (String.IsNullOrEmpty(file))
            {
                await msg.Channel.SendMessageAsync("Bad URL.").ConfigureAwait(false);
                return;
            }

            var image = await DetemplHelper(msg, file, pd).ConfigureAwait(false);
            if (String.IsNullOrEmpty(image))
            {
                await msg.Channel.SendMessageAsync("Detemplatization unsuccessful.").ConfigureAwait(false);
                return;
            }
            await msg.Channel.SendFileAsync(image).ConfigureAwait(false);

            File.Delete(file);
            File.Delete(image);
        }

        private async Task TemplateCmd(SocketMessage msg)
        {
            var words = msg.Content.Split(" ");
            if (words.Length == 1)
            {
                return;
            }
            var r = (msg.Author as SocketGuildUser).Roles.Select(r => r.Id);

            if (words.Length > 1)
            {
                switch (words[1])
                {
                    case "list":
                        await TemplateListCmd(msg).ConfigureAwait(false);
                        return;
                    case "add" when words.Length > 3:
                        //if (!r.Contains(_pxlsOfficerId))
                        //    return;
                        //await TemplateAddCmd(msg).ConfigureAwait(false);
                        return;
                    case "del" when words.Length == 3:
                        if (!r.Contains(_pxlsOfficerId))
                            return;
                        await TemplateDelCmd(msg).ConfigureAwait(false);
                        return;
                    case "get":
                        await TemplateGetCmd(msg).ConfigureAwait(false);
                        return;
                    case "detempl":
                        await DetemplatizeCmd(msg).ConfigureAwait(false);
                        return;
                    case "make":
                        await TemplateMakeCmd(msg).ConfigureAwait(false);
                        return;
                    default:
                        return;
                }
            }
        }

        private async Task TemplateMakeCmd(SocketMessage msg)
        {
            var words = msg.Content.Split(" ");
            if (msg.Attachments.Any())
            {
                switch (words.Length)
                {
                    case 2:
                        await TemplateMakeFromAttachment(msg).ConfigureAwait(false);
                        return;
                    case 3:
                        await TemplateMakeFromAttachmentSymbol(msg).ConfigureAwait(false);
                        return;
                    case 4:
                        await TemplateMakeFromAttachmentSymbolPalette(msg).ConfigureAwait(false);
                        return;
                    default:
                        break;
                }
            }
            else if (words.Length > 2 && words.Length < 6)
            {
                switch (words.Length)
                {
                    case 3:
                        await TemplateMakeFromURL(msg).ConfigureAwait(false);
                        break;
                    case 4:
                        await TemplateMakeFromURLSymbol(msg).ConfigureAwait(false);
                        break;
                    case 5:
                        await TemplateMakeFromURLSymbolPalette(msg).ConfigureAwait(false);
                        break;
                    default:
                        break;
                }
            }
            else 
            {
                await msg.Channel.SendMessageAsync("Unrecognized command format.").ConfigureAwait(false);
            }
        }

        private async Task TemplateMakeFromURL(SocketMessage msg)
        {
            var words = msg.Content.Split(" ");
            var url = words[2];
            var file = await GetTemplateFilePathByUrl(url).ConfigureAwait(false);
            if (String.IsNullOrEmpty(file))
            {
                await msg.Channel.SendMessageAsync("Bad URL.").ConfigureAwait(false);
                return;
            }

            IEnumerable<Rgba32> pd;

            var canvasData = await GetCanvasData(_infoDataUrl, _boardDataUrl).ConfigureAwait(false);
            pd = canvasData.Palette.Values;

            var image = await DetemplHelper(msg, file, pd);
            if (String.IsNullOrEmpty(image))
            {
                await msg.Channel.SendMessageAsync("Detemplatization unsuccessful.").ConfigureAwait(false);
                return;
            }

            (string template, int width) = CreateDottedTemplate(image);

            if (String.IsNullOrEmpty(template))
            {
                await msg.Channel.SendMessageAsync("Templatization unsuccessful.").ConfigureAwait(false);
            }

            ulong id = (await msg.Channel.SendFileAsync(template).ConfigureAwait(false)).Id;
            File.Delete(template);
            var templateUrl = Uri.EscapeDataString((await msg.Channel.GetMessageAsync(id).ConfigureAwait(false)).Attachments.First().Url);
            var templateString = $"https://pxls.space/#template={templateUrl}&tw={width}&ox=0&oy=0&x=0&y=0&scale=3&oo=1";
            await msg.Channel.SendMessageAsync(templateString).ConfigureAwait(false);
        }

        private async Task TemplateMakeFromURLSymbol(SocketMessage msg)
        {
            var words = msg.Content.Split(" ");
            var symbolType = words[2];
            var url = words[3];
            var file = await GetTemplateFilePathByUrl(url).ConfigureAwait(false);
            if (String.IsNullOrEmpty(file))
            {
                await msg.Channel.SendMessageAsync("Bad URL.").ConfigureAwait(false);
                return;
            }

            IEnumerable<Rgba32> pd;

            if (symbolType != "dotted")
            {
                await msg.Channel.SendMessageAsync("Unsupported symbol type").ConfigureAwait(false);
                return;
            }

            var canvasData = await GetCanvasData(_infoDataUrl, _boardDataUrl).ConfigureAwait(false);
            pd = canvasData.Palette.Values;

            var image = await DetemplHelper(msg, file, pd);
            if (String.IsNullOrEmpty(image))
            {
                await msg.Channel.SendMessageAsync("Detemplatization unsuccessful.").ConfigureAwait(false);
                return;
            }

            var template = String.Empty;
            int width = 0;
            switch (symbolType)
            {
                case "dotted":
                    (template, width) = CreateDottedTemplate(image);
                    break;
                default:
                    break;
            }

            if (String.IsNullOrEmpty(template))
            {
                await msg.Channel.SendMessageAsync("Templatization unsuccessful.").ConfigureAwait(false);
            }

            ulong id = (await msg.Channel.SendFileAsync(template).ConfigureAwait(false)).Id;
            File.Delete(template);
            var templateUrl = Uri.EscapeDataString((await msg.Channel.GetMessageAsync(id).ConfigureAwait(false)).Attachments.First().Url);
            var templateString = $"https://pxls.space/#template={templateUrl}&tw={width}&ox=0&oy=0&x=0&y=0&scale=3&oo=1";
            await msg.Channel.SendMessageAsync(templateString).ConfigureAwait(false);
        }

        private async Task TemplateMakeFromURLSymbolPalette(SocketMessage msg)
        {
            var words = msg.Content.Split(" ");
            var symbolType = words[2];
            var paletteName = words[3];
            var url = words[4];
            var file = await GetTemplateFilePathByUrl(url).ConfigureAwait(false);
            if (String.IsNullOrEmpty(file))
            {
                await msg.Channel.SendMessageAsync("Bad URL.").ConfigureAwait(false);
                return;
            }

            IEnumerable<Rgba32> pd;

            if (symbolType != "dotted")
            {
                await msg.Channel.SendMessageAsync("Unsupported symbol type").ConfigureAwait(false);
                return;
            }

            var q = _moduleConfig.Palettes.Where(p => p.Name == paletteName);
            if (!q.Any())
            {
                await msg.Channel.SendMessageAsync("Specified palette not found. Using current pxls.space palette.").ConfigureAwait(false);
                var canvasData = await GetCanvasData(_infoDataUrl, _boardDataUrl).ConfigureAwait(false);
                pd = canvasData.Palette.Values;
            }
            else
            {
                var list = new List<Rgba32>();
                foreach (var c in q.First().Colours)
                    list.Add(new Rgba32(c.Red, c.Green, c.Blue));
                pd = list;
            }

            var image = await DetemplHelper(msg, file, pd);
            if (String.IsNullOrEmpty(image))
            {
                await msg.Channel.SendMessageAsync("Detemplatization unsuccessful.").ConfigureAwait(false);
                return;
            }

            var template = String.Empty;
            int width = 0;
            switch (symbolType)
            {
                case "dotted":
                    (template, width) = CreateDottedTemplate(image);
                    break;
                default:
                    break;
            }

            if (String.IsNullOrEmpty(template))
            {
                await msg.Channel.SendMessageAsync("Templatization unsuccessful.").ConfigureAwait(false);
            }

            ulong id = (await msg.Channel.SendFileAsync(template).ConfigureAwait(false)).Id;
            File.Delete(template);
            var templateUrl = Uri.EscapeDataString((await msg.Channel.GetMessageAsync(id).ConfigureAwait(false)).Attachments.First().Url);
            var templateString = $"https://pxls.space/#template={templateUrl}&tw={width}&ox=0&oy=0&x=0&y=0&scale=3&oo=1";
            await msg.Channel.SendMessageAsync(templateString).ConfigureAwait(false);
        }

        private async Task TemplateMakeFromAttachment(SocketMessage msg)
        {
            var words = msg.Content.Split(" ");
            var url = msg.Attachments.First().Url;
            var file = await GetTemplateFilePathByUrl(url).ConfigureAwait(false);
            if (String.IsNullOrEmpty(file))
            {
                await msg.Channel.SendMessageAsync("Bad URL.").ConfigureAwait(false);
                return;
            }

            IEnumerable<Rgba32> pd;

            var canvasData = await GetCanvasData(_infoDataUrl, _boardDataUrl).ConfigureAwait(false);
            pd = canvasData.Palette.Values;

            var image = await DetemplHelper(msg, file, pd);
            if (String.IsNullOrEmpty(image))
            {
                await msg.Channel.SendMessageAsync("Detemplatization unsuccessful.").ConfigureAwait(false);
                return;
            }

            (string template, int width) = CreateDottedTemplate(image);

            if (String.IsNullOrEmpty(template))
            {
                await msg.Channel.SendMessageAsync("Templatization unsuccessful.").ConfigureAwait(false);
            }

            ulong id = (await msg.Channel.SendFileAsync(template).ConfigureAwait(false)).Id;
            File.Delete(template);
            var templateUrl = Uri.EscapeDataString((await msg.Channel.GetMessageAsync(id).ConfigureAwait(false)).Attachments.First().Url);
            var templateString = $"https://pxls.space/#template={templateUrl}&tw={width}&ox=0&oy=0&x=0&y=0&scale=3&oo=1";
            await msg.Channel.SendMessageAsync(templateString).ConfigureAwait(false);

        }


        private async Task TemplateMakeFromAttachmentSymbol(SocketMessage msg)
        {
            var words = msg.Content.Split(" ");
            var symbolType = words[2];
            var url = msg.Attachments.First().Url;
            var file = await GetTemplateFilePathByUrl(url).ConfigureAwait(false);
            if (String.IsNullOrEmpty(file))
            {
                await msg.Channel.SendMessageAsync("Bad URL.").ConfigureAwait(false);
                return;
            }

            IEnumerable<Rgba32> pd;

            if (symbolType != "dotted")
            {
                await msg.Channel.SendMessageAsync("Unsupported symbol type").ConfigureAwait(false);
                return;
            }

            var canvasData = await GetCanvasData(_infoDataUrl, _boardDataUrl).ConfigureAwait(false);
            pd = canvasData.Palette.Values;

            var image = await DetemplHelper(msg, file, pd);
            if (String.IsNullOrEmpty(image))
            {
                await msg.Channel.SendMessageAsync("Detemplatization unsuccessful.").ConfigureAwait(false);
                return;
            }

            var template = String.Empty;
            int width = 0;
            switch (symbolType)
            {
                case "dotted":
                    (template, width) = CreateDottedTemplate(image);
                    break;
                default:
                    break;
            }

            if (String.IsNullOrEmpty(template))
            {
                await msg.Channel.SendMessageAsync("Templatization unsuccessful.").ConfigureAwait(false);
            }

            ulong id = (await msg.Channel.SendFileAsync(template).ConfigureAwait(false)).Id;
            File.Delete(template);
            var templateUrl = Uri.EscapeDataString((await msg.Channel.GetMessageAsync(id).ConfigureAwait(false)).Attachments.First().Url);
            var templateString = $"https://pxls.space/#template={templateUrl}&tw={width}&ox=0&oy=0&x=0&y=0&scale=3&oo=1";
            await msg.Channel.SendMessageAsync(templateString).ConfigureAwait(false);
        }

        private async Task TemplateMakeFromAttachmentSymbolPalette(SocketMessage msg)
        {
            var words = msg.Content.Split(" ");
            var symbolType = words[2];
            var paletteName = words[3];
            var url = msg.Attachments.First().Url;
            var file = await GetTemplateFilePathByUrl(url).ConfigureAwait(false);
            if (String.IsNullOrEmpty(file))
            {
                await msg.Channel.SendMessageAsync("Bad URL.").ConfigureAwait(false);
                return;
            }

            IEnumerable<Rgba32> pd;

            if (symbolType != "dotted")
            {
                await msg.Channel.SendMessageAsync("Unsupported symbol type").ConfigureAwait(false);
                return;
            }

            var q = _moduleConfig.Palettes.Where(p => p.Name == paletteName);
            if (!q.Any())
            {
                await msg.Channel.SendMessageAsync("Specified palette not found. Using current pxls.space palette.").ConfigureAwait(false);
                var canvasData = await GetCanvasData(_infoDataUrl, _boardDataUrl).ConfigureAwait(false);
                pd = canvasData.Palette.Values;
            }
            else
            {
                var list = new List<Rgba32>();
                foreach (var c in q.First().Colours)
                    list.Add(new Rgba32(c.Red, c.Green, c.Blue));
                pd = list;
            }

            var image = await DetemplHelper(msg, file, pd);
            if (String.IsNullOrEmpty(image))
            {
                await msg.Channel.SendMessageAsync("Detemplatization unsuccessful.").ConfigureAwait(false);
                return;
            }

            var template = String.Empty;
            int width = 0;
            switch (symbolType)
            {
                case "dotted":
                    (template, width) = CreateDottedTemplate(image);
                    break;
                default:
                    break;
            }
            
            if (String.IsNullOrEmpty(template))
            {
                await msg.Channel.SendMessageAsync("Templatization unsuccessful.").ConfigureAwait(false);
            }

            ulong id = (await msg.Channel.SendFileAsync(template).ConfigureAwait(false)).Id;
            File.Delete(template);
            var templateUrl = Uri.EscapeDataString((await msg.Channel.GetMessageAsync(id).ConfigureAwait(false)).Attachments.First().Url);
            var templateString = $"https://pxls.space/#template={templateUrl}&tw={width}&ox=0&oy=0&x=0&y=0&scale=3&oo=1";
            await msg.Channel.SendMessageAsync(templateString).ConfigureAwait(false);
        }

        private (string, int) CreateDottedTemplate(string srcFile)
        {
            using var src = SixLabors.ImageSharp.Image.Load(srcFile).CloneAs<Rgba32>();
            using var template = new Image<Rgba32>(src.Width * 3, src.Height * 3);

            for (int i = 0; i < src.Width; i++)
            {
                for (int j = 0; j < src.Height; j++)
                {
                    template[i * 3 + 1, j * 3 + 1] = src[i, j];
                }
            }

            template[0, 0] = new Rgba32(
                template[0, 0].R,
                template[0, 0].G,
                template[0, 0].B,
                (byte)((src[0, 0].A > 128) ? 255 - 3 : 3));

            string destPath = Path.Combine(_modulePath, Path.GetFileNameWithoutExtension(srcFile) + "-template.png");

            try
            {
                using (var fs = new FileStream(destPath, FileMode.Create, FileAccess.ReadWrite))
                    template.SaveAsPng(fs);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EXCEPT][PXLS] Catched :{ex.Message}");
                Console.WriteLine($"[EXCEPT][PXLS] Stack   :{ex.StackTrace}");
                destPath = String.Empty;
            }

            return (destPath, src.Width);
        }

        private async Task TemplateGetCmd(SocketMessage msg)
        { 
            var words = msg.Content.Split(" ");
            var name = words[2];
            var q = _moduleConfig.Templates.Where(t => t.Name == name);
            if (!q.Any())
            {
                await msg.Channel.SendMessageAsync("Specified template not found.").ConfigureAwait(false);
                return;
            }
            var t = q.First();
            string output = String.Empty;
            output += $@"`Template {name} info:`" + Environment.NewLine;
            output += Environment.NewLine;
            output += $@"`Id : {t.Id}`" + Environment.NewLine;
            output += $@"`Name : {t.Name}`" + Environment.NewLine;
            output += $@"`URL : `{t.Url}" + Environment.NewLine;
            output += $@"`Image URL : `{t.SourceUrl}" + Environment.NewLine;
            output += $@"`Added by : {t.AddedBy}`" + Environment.NewLine;
            // TODO : Implement Palette ID
            output += $@"`Palette ID : {"Not implemented"}`" + Environment.NewLine;

            var guild = (msg.Author as SocketGuildUser).Guild;
            string iconUrl = guild.IconUrl;
            var eba = new EmbedAuthorBuilder
            {
                Name = @"Shining Armor"
                //, IconUrl = @"https://cdn.discordapp.com/avatars/335004246007218188/3094a7be163d3cd1d03278b53c8f08eb.png"
            };

            var efb = new EmbedFieldBuilder
            {
                IsInline = false,
                Name = $"{name}",
                Value = output
            };

            var efob = new EmbedFooterBuilder
            {
                Text = "Мы в котле были в первую пиксельную."
            };

            var eb = new EmbedBuilder
            {
                Author = eba,
                Color = Discord.Color.Green,
                ThumbnailUrl = iconUrl,
                Title = "Шаблон :",
                Timestamp = DateTime.Now,
                Footer = efob
            };

            eb.Fields.Add(efb);
            
            await msg.Channel.SendMessageAsync(String.Empty, false, eb.Build()).ConfigureAwait(false);
        }
        private async Task TemplateDelCmd(SocketMessage msg)
        {
            var words = msg.Content.Split(" ");
            var q = _moduleConfig.Templates.Where(t => t.Name == words[2]);
            if (q.Any())
            {
                if (_moduleConfig.Templates.Remove(q.First()))
                {
                    await SaveConfig().ConfigureAwait(false);
                    await msg.Channel.SendMessageAsync($"Template {words[2]} deleted.").ConfigureAwait(false);
                }
            }
            else
                return;

        }

        private async Task TemplateAddCmd(SocketMessage msg)
        {
            var words = msg.Content.Split(" ");
            var name = words[2];
            var url = words[3];
#if DEBUG
            Console.WriteLine($"[DEBUG][PXLS] Template Add Cmd URL :{url}");
#endif
            if (!Uri.IsWellFormedUriString(url, UriKind.RelativeOrAbsolute))
            {
                msg.Channel.SendMessageAsync("Bad template URL.");
#if DEBUG
                Console.WriteLine("[DEBUG][PXLS] URL BAD.");
#endif
                return;
            }
#if DEBUG
            Console.WriteLine("[DEBUG][PXLS] URL OK.");
#endif
            if (_moduleConfig.Templates.Where(t => t.Name == name).Any())
            {
                await msg.Channel.SendMessageAsync("Template with this name already exists.").ConfigureAwait(false);
                return;
            }

            var templateRegex = new Regex(_templateRegexPattern);
            var templateMatch = templateRegex.Match(url);

            if (!Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var _))
#if DEBUG
            {
                Console.WriteLine($"[DEBUG][PXLS-ALERTS] Invalid uri :{url}");
                return;
            }
#else
                return;
#endif
            if (!Uri.TryCreate(templateMatch.Groups["uri"].Value, UriKind.RelativeOrAbsolute, out var _))
#if DEBUG
            {
                Console.WriteLine($"[DEBUG][PXLS-ALERTS] Invalid uri :{templateMatch.Groups["uri"].Value}");
                return;
            }
#else
                return;
#endif
            var template = new Template()
            {
                Id = _moduleConfig.Templates.Select(t => t.Id).DefaultIfEmpty(0).Max() + 1,
                Name = name,
                Url = url,
                SourceUrl = Uri.UnescapeDataString(templateMatch.Groups["uri"].Value),
                AddedBy = $"{msg.Author.Username}#{msg.Author.DiscriminatorValue}"
                // TODO : Add PaletteId
                //, PaletteId = ...
            };

            _moduleConfig.Templates.Add(template);

            await SaveConfig().ConfigureAwait(false);
            await msg.Channel.SendMessageAsync($"Template {name} added.").ConfigureAwait(false);
        }

        private async Task TemplateListCmd(SocketMessage msg)
        {
            string output = String.Empty;
            List<string> outputList = new List<string>();
            foreach (var template in _moduleConfig.Templates)
            {
                outputList.Add($"`Template id:{template.Id,3}  name:{template.Name,20}  added by:{template.AddedBy,20}`");
            }

            foreach (var line in outputList)
            {
                if (output.Length + line.Length > 1950)
                {
                    await msg.Channel.SendMessageAsync(output).ConfigureAwait(false);
                    output = String.Empty;
                }

                output += line + Environment.NewLine;
            }

            if (!String.IsNullOrEmpty(output) && !String.IsNullOrEmpty(output))
                await msg.Channel.SendMessageAsync(output).ConfigureAwait(false);
        }

        private async Task PaletteCmd(SocketMessage msg)
        {
            var words = msg.Content.Split(" ");
            if (words.Length == 1)
            {
                return;
            }
            var r = (msg.Author as SocketGuildUser).Roles.Select(r => r.Id);

            switch (words[1])
            {
                case "list":
                    await PaletteListCmd(msg).ConfigureAwait(false);
                    return;
                case "add" when words.Length > 2:
                    if (!r.Contains(_pxlsOfficerId))
                        return;
                    await PaletteAddCmd(msg).ConfigureAwait(false);
                    break;
                case "get" when words.Length > 2:
                    await PaletteGetCmd(msg).ConfigureAwait(false);
                    break;
                case "del" when words.Length == 3:
                    if (!r.Contains(_pxlsOfficerId))
                        return; 
                    await PaletteDelCmd(msg).ConfigureAwait(false);
                    break;
                default:
                    return;
            }
        }
        private async Task PaletteDelCmd(SocketMessage msg)
        {
            var words = msg.Content.Split(" ");
            var q = _moduleConfig.Palettes.Where(p => p.Name == words[2]);
            if (q.Any())
            {
                if (_moduleConfig.Palettes.Remove(q.First()))
                {
                    await SaveConfig().ConfigureAwait(false);
                    await msg.Channel.SendMessageAsync($"Palette {words[2]} deleted.").ConfigureAwait(false);
                }
            }
        }

        private async Task PaletteGetCmd(SocketMessage msg)
        {
            var words = msg.Content.Split(" ");
            var name = words[2];
            var q = _moduleConfig.Palettes.Where(p => p.Name == name);
            if (q.Any())
            {
                if (words.Length == 3)
                {
                    await SendNetTextPalette(msg, q.First()).ConfigureAwait(false);
                    return;
                }
                else if (words.Length == 4)
                {
                    switch (words[3])
                    {
                        case ".net":
                        case "text":
                            await SendNetTextPalette(msg, q.First()).ConfigureAwait(false);
                            return;
                        case "file":
                            await SendNetFilePalette(msg, q.First()).ConfigureAwait(false);
                            break;
                        default:
                            break;
                    }
                }
                else if (words.Length == 5)
                { }
                else
                    return;
            }
            else
            {
                await msg.Channel.SendMessageAsync("Specified palette not found.").ConfigureAwait(false);
            }
        }

        private async Task SendNetTextPalette(SocketMessage msg, Palette p)
        {
            string output = @"```; pxls.space palette file for Paint.NET " + Environment.NewLine;
            var outputList = PaletteToNetStrings(p);
                
            foreach (var str in outputList)
            {
                if (output.Length + str.Length > 1950)
                {
                    output += "```";
                    await msg.Channel.SendMessageAsync(output).ConfigureAwait(false);
                    output = "```";
                }
                output += str + Environment.NewLine;
            }
            output += "```";
            await msg.Channel.SendMessageAsync(output).ConfigureAwait(false);
        }

        private async Task SendNetFilePalette(SocketMessage msg, Palette p)
        {
            string output = @"; pxls.space palette file for Paint.NET " + Environment.NewLine;
            var outputList = PaletteToNetStrings(p);

            foreach (var str in outputList)
            {
                output += str + Environment.NewLine;
            }

            try
            {
                string filePath = Path.Combine(_modulePath, $"pnet-palette-{Guid.NewGuid()}.txt");
                File.WriteAllText(filePath, output);
                await msg.Channel.SendFileAsync(filePath).ConfigureAwait(false);
                File.Delete(filePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EXCEPT][PXLS] Catched:{ex.Message}");
                Console.WriteLine($"[EXCEPT][PXLS] Stack  :{ex.StackTrace}");
                throw;
            }
        }

        private List<string> PaletteToNetStrings(Palette p)
        {
            List<string> outputList = new List<string>();
            foreach (var c in p.Colours)
            {
                outputList.Add($"FF{c.Red:X2}{c.Green:X2}{c.Blue:X2};");
            }
            return outputList;
        }

        private async Task PaletteAddCmd(SocketMessage msg)
        {
            var words = msg.Content.Split(" ");
            if (msg.Attachments.Count == 1)
            {
                try
                {
                    string name = words[2];
                    if (_moduleConfig.Palettes.Where(p => p.Name == name).Any())
                    {
                        await msg.Channel.SendMessageAsync("Palette with this name already exists.").ConfigureAwait(false);
                        return;
                    }
                    var response = await _hc.GetAsync(msg.Attachments.First().Url);
                    var dataStr = await response.Content.ReadAsStringAsync();
                    Colour[] colours = null;
                    if (dataStr.Contains("Paint.NET"))
                    {
                        colours = ParsePaintNETFile(dataStr);
                    }
                    else
                    {
                        // TODO : other formats
                    }

                    if (colours == null)
                        return;
                    var palette = new Palette()
                    {
                        Id = _moduleConfig.Palettes.Select(p => p.Id).DefaultIfEmpty(0).Max() + 1,
                        Name = name,
                        Colours = colours
                    };

                    _moduleConfig.Palettes.Add(palette);

                    await SaveConfig().ConfigureAwait(false);

                    await msg.Channel.SendMessageAsync($"Palette {name} added.").ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EXCEPT][PXLS] Catched :{ex.Message}");
                    Console.WriteLine($"[EXCEPT][PXLS] Stack   :{ex.StackTrace}");
                    throw;
                }
            }
            else if (msg.Attachments.Count == 0)
            { 
                // fisting is 300 bucks
            }
            else
                return;
        }

        private async Task SaveConfig()
        {
            try
            {
#if DEBUG
                Console.WriteLine($"[DEBUG][PXLS] SaveConfig Triggered.");
                Console.WriteLine($"[DEBUG][PXLS] _moduleConfig.Stats is null: {_moduleConfig.Stats == null}");
                if (_moduleConfig.Stats != null)
                {
                    Console.WriteLine($"[DEBUG][PXLS] _moduleConfig.ChannelId: {_moduleConfig.Stats.ChannelId}");
                    Console.WriteLine($"[DEBUG][PXLS] _moduleConfig.Interval: {_moduleConfig.Stats.Interval}");
                }
#endif
                var bytes = JsonSerializer.SerializeToUtf8Bytes<PxlsAlertsModuleConfig>(_moduleConfig, new JsonSerializerOptions() { WriteIndented = true });
                await File.WriteAllBytesAsync(_moduleConfigFilePath, bytes).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EXCEPT][PXLS] Catched :{ex.Message}");
                Console.WriteLine($"[EXCEPT][PXLS] Stack   :{ex.StackTrace}");
                throw;
            }
        }

        private Colour[] ParsePaintNETFile(string data)
        {
            Regex colorRegex = new Regex("FF(?<r>[0-9a-fA-F]{2})(?<g>[0-9a-fA-F]{2})(?<b>[0-9a-fA-F]{2})", RegexOptions.Compiled);
            var matches = colorRegex.Matches(data);
            byte id = 0;
            Colour[] colours = new Colour[matches.Count];

            foreach (Match match in matches)
            {
                if (byte.TryParse(match.Groups["r"].Value, NumberStyles.HexNumber, null, out var r))
                    if (byte.TryParse(match.Groups["g"].Value, NumberStyles.HexNumber, null, out var g))
                        if (byte.TryParse(match.Groups["b"].Value, NumberStyles.HexNumber, null, out var b))
                            colours[id++] = new Colour() { Red = r, Green = g, Blue = b };
            }

            return colours;
        }

        private async Task PaletteListCmd(SocketMessage msg)
        {
            string output = String.Empty;
            List<string> outputList = new List<string>();
            foreach (var palette in _moduleConfig.Palettes)
            {
                outputList.Add($"`Palette id:{palette.Id,5}   name:{palette.Name,20}  colours: {palette.Colours.Length}`");
            }
            
            foreach (var line in outputList)
            {
                if (output.Length + line.Length > 1950)
                {
                    await msg.Channel.SendMessageAsync(output).ConfigureAwait(false);
                    output = String.Empty;
                }

                output += line + Environment.NewLine;
            }

            if (!String.IsNullOrEmpty(output) && !String.IsNullOrEmpty(output))
                await msg.Channel.SendMessageAsync(output).ConfigureAwait(false);
        }

        private async Task StatusCmd(SocketMessage msg)
        {
#if DEBUG
            Console.WriteLine("[DEBUG][PXLS-ALERTS] Entered StatusCmd.");
#endif
            string[] words = msg.Content.Split(" ");
            string inputUri = words[1];
#if DEBUG
            Console.WriteLine($"[DEBUG][PXLS-ALERTS] words[1] :{words[1]}");
#endif
            await GetStatus(inputUri, msg.Channel, true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _alertTimer.Stop();
                _alertTimer.Dispose();
            }
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        static async Task<CanvasData> GetCanvasData(string infoUri, string boardUri)
        {
            var paletteRegex = new Regex(@"^#(?<r>[0-9a-fA-F]{2})(?<g>[0-9a-fA-F]{2})(?<b>[0-9a-fA-F]{2})$", RegexOptions.Compiled);
            var paletteDict = new Dictionary<byte, Rgba32>();
            int width = 0;
            int height = 0;

            var response = await _hc.GetAsync(infoUri);
            response.EnsureSuccessStatusCode();
            var resultStream = await response.Content.ReadAsStreamAsync();
            var jsonRoot = (await JsonDocument.ParseAsync(resultStream)).RootElement;

            if (jsonRoot.TryGetProperty("palette", out var paletteVal))
            {
                byte count = 0;
                foreach (var el in paletteVal.EnumerateArray())
                {
                    var elStrVal = el.GetString();
                    var match = paletteRegex.Match(elStrVal);

                    if (byte.TryParse(match.Groups["r"].Value, NumberStyles.HexNumber, null, out byte r))
                        if (byte.TryParse(match.Groups["g"].Value, NumberStyles.HexNumber, null, out byte g))
                            if (byte.TryParse(match.Groups["b"].Value, NumberStyles.HexNumber, null, out byte b))
                                paletteDict.Add(count++, new Rgba32(r, g, b));
                }
                paletteDict.Add(255, new Rgba32(0, 0, 0, 0));
            }

            if (jsonRoot.TryGetProperty("width", out var widthEl))
            {
                if (widthEl.TryGetInt32(out int widthVal))
                {
                    width = widthVal;
                }
            }

            if (jsonRoot.TryGetProperty("height", out var heightEl))
            {
                if (heightEl.TryGetInt32(out int heightVal))
                {
                    height = heightVal;
                }
            }

            response = await _hc.GetAsync(boardUri);
            response.EnsureSuccessStatusCode();
            var canvasData = await response.Content.ReadAsByteArrayAsync();
            var boardData = new Image<Rgba32>(width, height);
            for (int i = 0; i < width; i++)
                for (int j = 0; j < height; j++)
                    boardData[i, j] = paletteDict[canvasData[i + j * width]];

            return new CanvasData { Width = width, Height = height, Palette = paletteDict, Canvas = boardData };
        }

        static Image<Rgba32> Detemplatize(Image<Rgba32> src, int symbolSize, IEnumerable<Rgba32> palette)
        {
            if (symbolSize == 1)
                return src.Clone();

            var outImg = new Image<Rgba32>(src.Width / symbolSize, src.Height / symbolSize);

            for (int i = 0; i < src.Width; i += symbolSize)
            {
                for (int j = 0; j < src.Height; j += symbolSize)
                {
                    outImg[i / symbolSize, j / symbolSize] = GetColour(src, symbolSize, i, j, palette);
                }
            }

            return outImg;
        }

        static int GetTemplateSymbolSizeFromMetadata(Image<Rgba32> template)
        {
            int a = template[0, 0].A;

            if (a > 128)
                return 255 - a;
            else
                return a;
        }

        static int GetTemplateSymbolSize(Image<Rgba32> template, IEnumerable<Rgba32> palette)
        {
            Rgba32 c = Rgba32.Transparent;

            for (int tempSize = 21; tempSize > 2; tempSize--)
            {
                if ((template.Width % tempSize != 0) || (template.Height % tempSize != 0))
                    continue;

                for (int i = 0; i < template.Width; i += tempSize)
                {
                    for (int j = 0; j < template.Height; j += tempSize)
                    {
                        c = GetColour(template, tempSize, i, j, palette);
                        if (c == Rgba32.Transparent)
                            break;
                    }
                    if (c == Rgba32.Transparent)
                        break;
                }

                if (c != Rgba32.Transparent)
                    return tempSize;
            }

            return 1;
        }

        static Rgba32 GetColour(Image<Rgba32> template, int tempSize, int i, int j, IEnumerable<Rgba32> palette)
        {
            Rgba32 refColour = Rgba32.Transparent;
            bool trFlag = true;
            for (int ii = 0; ii < tempSize; ii++)
            {
                for (int jj = 0; jj < tempSize; jj++)
                {
                    var c = template[i + ii, j + jj];
                    if (c.A == 0)
                        continue;
                    if (!palette.Contains(c))
                        continue;

                    trFlag = false;
                    
                    if (refColour == Rgba32.Transparent)
                    {
                        refColour = c;
                    }
                    else if (c != refColour)
                    {
                        return Rgba32.Transparent;
                    }
                }
            }

            if (trFlag)
                refColour = new Rgba32(0, 0, 0, 0);

            return refColour;
        }
    }

    public struct Palette
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; }
        [JsonPropertyName("colours")]
        public Colour[] Colours { get; set; }
    }
    public struct Colour
    {
        [JsonPropertyName("r")]
        public byte Red { get; set; }
        [JsonPropertyName("g")]
        public byte Green { get; set; }
        [JsonPropertyName("b")]
        public byte Blue { get; set; }
    }

    public struct Template
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; }
        [JsonPropertyName("srcurl")]
        public string SourceUrl { get; set; }
        [JsonPropertyName("url")]
        public string Url { get; set; }
        [JsonPropertyName("paletteid")]
        public int PaletteId { get; set; }
        [JsonPropertyName("addedby")]
        public string AddedBy { get; set; }
    }

    public class Alerts
    {
        [JsonPropertyName("channelid")]
        public ulong ChannelId { get; set; }
        [JsonPropertyName("interval")]
        public int Interval { get; set; }
        [JsonPropertyName("templateids")]
        public List<int> TemplateIds { get; set; }
        public Alerts()
        {
            TemplateIds = new List<int>();
        }
    }

    public class Stats
    {
        [JsonPropertyName("channelid")]
        public ulong ChannelId { get; set; }
        [JsonPropertyName("interval")]
        public int Interval { get; set; }
        [JsonPropertyName("auto")]
        public bool Auto { get; set; }
        [JsonPropertyName("users")]
        public List<User> Users { get; set; }
        [JsonPropertyName("lastdownload")]
        public DateTime LastDownload { get; set; }

        public Stats()
        {
            Users = new List<User>();
        }
    }

    public class User
    {
        [JsonPropertyName("userid")]
        public ulong UserId { get; set; }
        [JsonPropertyName("canvasname")]
        public string CanvasName { get; set; }
        [JsonPropertyName("globalcount")]
        public int GlobalCount { get; set; }
        [JsonPropertyName("canvascount")]
        public int CanvasCount { get; set; }
        [JsonPropertyName("globalplace")]
        public int GlobalPlace { get; set; }
        [JsonPropertyName("canvasplace")]
        public int CanvasPlace { get; set; }
        [JsonPropertyName("canvasprevcount")]
        public int CanvasPreviousCount { get; set; }
    }

    public class PxlsAlertsModuleConfig
    { 
        [JsonPropertyName("palettes")]
        public List<Palette> Palettes { get; set; }
        [JsonPropertyName("templates")]
        public List<Template> Templates { get; set; }
        [JsonPropertyName("alerts")]
        public Alerts Alerts { get; set; }
        [JsonPropertyName("stats")]
        public Stats Stats { get; set; }
        [JsonExtensionData]
        public Dictionary<string, object> ExtensionData { get; set; }
        public PxlsAlertsModuleConfig()
        {
            Alerts = new Alerts();
            Stats = new Stats();
            Palettes = new List<Palette>();
            Templates = new List<Template>();
            ExtensionData = new Dictionary<string, object>();
        }
    }

    public class StatsJsonUser
    {
        [JsonPropertyName("username")]
        public string Username { get; set; }
        [JsonPropertyName("pixels")]
        public int Pixels { get; set; }
        [JsonPropertyName("place")]
        public int Place { get; set; }
    }

    public class TopListJson
    {
        [JsonPropertyName("alltime")]
        public List<StatsJsonUser> Alltime { get; set; }
        [JsonPropertyName("canvas")]
        public List<StatsJsonUser> Canvas { get; set; }
    }

    public class StatsJson
    {
        [JsonPropertyName("toplist")]
        public TopListJson TopList { get; set; }
        [JsonExtensionData]
        public Dictionary<string, object> ExtensionData { get; set; }
    }
}
