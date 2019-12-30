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
    public class Program 
    {
        #region DLL Import
        //[DllImport("Kernel32.dll")]
        //private static extern bool SetConsoleCtrlHandler(CtrlEventHandler handler, bool add);

        #endregion

        // Private members section

        private string _prefix;
        private string _token;
        private string _appBaseDirectory;

        private string _configFileName = "config.xml";
        private XDocument _config;

        private DiscordSocketClient _client;

        private ManualResetEvent _mre = new ManualResetEvent(false);

        private Dictionary<ulong, GuildBot> _guildBotsDict;

        private event CtrlEventHandler _ctrlEventHandler;

        // Public methods section

        public static void Main(string[] args)
            => new Program().MainAsync().GetAwaiter().GetResult();

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

                await LoadCoreConfig();

                await _client.LoginAsync(TokenType.Bot, _token);

                await _client.StartAsync();

                _client.Ready += 
                    () => { return Task.Run(ConfigGuilds); };

                AppDomain.CurrentDomain.ProcessExit += (o, e) => OnCtrlEvent(CtrlType.CTRL_C_EVENT);

                _client.MessageReceived += MessageReceived;

                //SetConsoleCtrlHandler(OnCtrlEvent, true);

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
            await LoadConfig();
        }

        private async Task ConfigGuilds()
        {
            // Debug
            // Console.WriteLine("ConfigGuilds Triggered");
            //
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
                );

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
                    {
                        using (var stream = File.Open(Path.Combine(_appBaseDirectory, _configFileName), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            _config = XDocument.Load(stream);
                        }
                    }
                    else
                    {
                        // TODO : Add default config creation.
                        throw new ApplicationException("Configuration file not found!");
                    }
                }
            );

            if (_config.Root.Name != "botconfig")
            {
                throw new ApplicationException("Configuration file not found.");
            }

            XElement tokenEl = _config.Root.Element("token");
            if (tokenEl == null)
            {
                throw new ApplicationException("Configuration file is missing token data.");
            }
            if (tokenEl.Value == String.Empty)
            {
                throw new ApplicationException("Configuration file is missing token data.");
            }

            _token = tokenEl.Value;


            XElement prefixEl = _config.Root.Element("coreprefix");
            if (prefixEl == null)
            {
                throw new ApplicationException("Configuration file is missing prefix data.");
            }
            if (prefixEl.Value == String.Empty)
            {
                throw new ApplicationException("Configuration file is missing prefix data.");
            }

            _prefix = prefixEl.Value;

        }
            
        private Task Log(LogMessage logMsg)
        {
            Console.WriteLine(logMsg.ToString());
            return Task.CompletedTask;
        }

        private async Task MessageReceived(SocketMessage msg)
        {
#if DEBUG
            //Console.WriteLine("[DEBUG][CORE] MessageReceived triggered.");
#endif
            if (msg.Channel is SocketGuildChannel scg)
            {
                ulong id = scg.Guild.Id;
                await Task.Run(
                    () =>
                    {
#if DEBUG
                        //Console.WriteLine($"[DEBUG][CORE] MessageReceived guild id :{id}.");
#endif
                        if (_guildBotsDict.TryGetValue(id, out var bot))
#if DEBUG
                        {
                            //Console.WriteLine($"[DEBUG][CORE] MessageReceived guildBot : {bot}.");
#endif
                            bot.Dispatch(msg);
#if DEBUG
                        }
#endif
                    }
                );
            }
        }
    }
}
