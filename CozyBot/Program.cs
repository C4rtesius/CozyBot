using System;
using System.IO;
using System.Xml.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Discord;
using Discord.WebSocket;

namespace CozyBot
{
    public class Program : IDisposable
    {
        // Private members section

        private string _prefix;
        private string _token;
        private string _appBaseDirectory;
        private string _configFileName = "config.xml";
        private XDocument _config;
        private DiscordSocketClient _client;
        private ManualResetEvent _mre = new ManualResetEvent(false);
        private Dictionary<ulong, GuildBot> _guildBotsDict;
        private bool disposedValue;

        private event CtrlEventHandler _ctrlEventHandler;

        // Public methods section

        public static async Task Main(string[] args)
        {
            using var p = new Program();
            await p.MainAsync().ConfigureAwait(false);
        }

        public async Task MainAsync()
        {
            try
            {
#if DEBUG
                Console.WriteLine($"[DEBUG][CORE] Current working dir :{Directory.GetCurrentDirectory()}");
                Console.WriteLine($"[DEBUG][CORE] Current AppDomain BaseDirectory :{AppDomain.CurrentDomain.BaseDirectory}");
#endif
                _guildBotsDict = new Dictionary<ulong, GuildBot>();

                _client = new DiscordSocketClient();
                _client.Log += Log;

                await LoadCoreConfig().ConfigureAwait(false);
                await _client.LoginAsync(TokenType.Bot, _token).ConfigureAwait(false);
                await _client.StartAsync().ConfigureAwait(false);

                _client.Ready += () => { return Task.Run(ConfigGuilds); };

                AppDomain.CurrentDomain.ProcessExit += (o, e) => OnCtrlEvent(CtrlType.CTRL_C_EVENT);

                _client.MessageReceived += MessageReceived;
                _client.UserJoined += UserJoined;

                _mre.WaitOne();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                Console.ReadKey();
            }
        }

        // Private methods section

        private async Task UserJoined(SocketGuildUser user)
        {
            if (user.Username.ToLower().Contains(@"twitter.com\h", System.StringComparison.InvariantCulture))
                try { await user.BanAsync(0, "Bot").ConfigureAwait(false); }
                catch { }
        }

        private void OnCtrlEvent(CtrlType type)
        {
#if DEBUG
            Console.WriteLine("[DEBUG][CORE] ProcessExit triggered.");
#endif
            _ctrlEventHandler.Invoke(type);
            _mre.Set();
        }

        private async Task LoadCoreConfig()
        {
            _appBaseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            await LoadConfig().ConfigureAwait(false);
        }

        private async Task ConfigGuilds()
        {
            var guilds = _client.Guilds;
            foreach (var guild in guilds)
            {
                ulong guildID = guild.Id;
                string guildPath = Path.Combine(_appBaseDirectory, guildID.ToString());
                string guildConfigPath = Path.Combine(guildPath, "config.xml");
                await Task.Run(
                    () =>
                    {
#if DEBUG
                        Console.WriteLine($"[DEBUG][CORE] guildPath:{guildPath}");
#endif
                        if (!Directory.Exists(guildPath))
                        {
#if DEBUG
                            Console.WriteLine($"[DEBUG][CORE] Creating guildPath:{guildPath}");
#endif
                            Directory.CreateDirectory(guildPath);
                        }
                        if (!File.Exists(guildConfigPath))
                        {
#if DEBUG
                            Console.WriteLine($"[DEBUG][CORE] Creating guild config:{guildConfigPath}");
#endif
                            GetDefaultGuildConfig().Save(guildConfigPath);
                        }
                    }
                ).ConfigureAwait(false);

                GuildBot newBot = new GuildBot(guild, guildPath, _client.CurrentUser.Id);

                _ctrlEventHandler += newBot.OnCtrlEvent;

                _guildBotsDict.TryAdd(guildID, newBot);
            }
        }

        private XDocument GetDefaultGuildConfig()
        {
            XDocument newConfig = new XDocument();
            XElement root =
                new XElement("guildbotconfig",
                    new XElement("coreprefix", _prefix),
                    new XElement("modules",
                        new XElement("usercite",
                            new XAttribute("on", Boolean.TrueString),
                            new XAttribute("prefix", "c!")
                        ),
                        new XElement("userimg",
                            new XAttribute("on", Boolean.TrueString),
                            new XAttribute("prefix", "i!")
                        ),
                        new XElement("archive",
                            new XAttribute("on", Boolean.TrueString),
                            new XAttribute("prefix", "x!")
                        )
                    )
                );
            newConfig.Add(root);

            return newConfig;
        }


        private async Task LoadConfig()
        {
            await Task.Run(
                () =>
                {
#if DEBUG
                    Console.WriteLine($"[DEBUG][CORE] Load Config Path :{Path.Combine(_appBaseDirectory, _configFileName)}");
#endif
                    if (File.Exists(Path.Combine(_appBaseDirectory, _configFileName)))
                        using (var stream = File.Open(Path.Combine(_appBaseDirectory, _configFileName), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            _config = XDocument.Load(stream);
                    else
                        throw new ApplicationException("Configuration file not found!");
                }
            ).ConfigureAwait(false);

            if (_config.Root.Name != "botconfig")
                throw new ApplicationException("Configuration file not found.");

            XElement tokenEl = _config.Root.Element("token");
            if (tokenEl == null)
                throw new ApplicationException("Configuration file is missing token data.");
            if (String.IsNullOrEmpty(tokenEl.Value))
                throw new ApplicationException("Configuration file is missing token data.");

            _token = tokenEl.Value;


            XElement prefixEl = _config.Root.Element("coreprefix");
            if (prefixEl == null)
                throw new ApplicationException("Configuration file is missing prefix data.");
            if (String.IsNullOrEmpty(prefixEl.Value))
                throw new ApplicationException("Configuration file is missing prefix data.");

            _prefix = prefixEl.Value;
        }

        private Task Log(LogMessage logMsg)
        {
            Console.WriteLine(logMsg.ToString());
            return Task.CompletedTask;
        }

        private async Task MessageReceived(SocketMessage msg)
        {
            if (msg.Channel is SocketGuildChannel scg)
            {
                ulong id = scg.Guild.Id;
                await Task.Run(
                    () =>
                    {
                        if (_guildBotsDict.TryGetValue(id, out var bot))
                            bot.Dispatch(msg);
                    }
                ).ConfigureAwait(false);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    if (_client != null)
                        _client.Dispose();
                    if (_mre != null)
                        _mre.Dispose();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
