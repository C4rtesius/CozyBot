using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Diagnostics;

using Discord;
using Discord.Audio;
using Discord.WebSocket;

namespace CozyBot
{
    public class MusicModule : IGuildModule
    {
        private static string _stringID => "MusicModule";
        private static string _moduleXmlName => "music";
        private static string _moduleFolder => @"music";

        private SocketGuild _guild;

        protected XElement _configEl;
        protected List<ulong> _adminIds;
        protected bool _isActive;
        protected List<IBotCommand> _useCommands = new List<IBotCommand>();
        protected static string _defaultPrefix = "m!";
        protected string _guildPath;
        protected string _prefix;
        private string _modulePath => Path.Combine(_guildPath, _moduleFolder);

        public string StringID => _stringID;
        public SocketGuild Guild => _guild;
        public bool IsActive => _isActive;
        public string ModuleXmlName => _moduleXmlName;

        public IEnumerable<IBotCommand> ActiveCommands
        {
            get
            {
                foreach (var cmd in _useCommands)
                    yield return cmd;
            }
        }

        public event ConfigChanged GuildBotConfigChanged
        {
            add
            {
                if (value != null)
                {
                    //_configChanged += value;
                }
            }
            remove
            {
                if (value != null)
                {
                    //_configChanged -= value;
                }
            }
        }

        public MusicModule(XElement configEl, List<ulong> adminIds, SocketGuild guild, string guildPath)
        {
            _guild = Guard.NonNull(guild, nameof(guild));
            _configEl = Guard.NonNull(configEl, nameof(configEl));
            _guildPath = Guard.NonNullWhitespaceEmpty(guildPath, nameof(guildPath));
            _adminIds = adminIds;

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

            if (_isActive)
                GenerateUseCommands();
            else
                _useCommands = new List<IBotCommand>();
        }

        private void GenerateUseCommands()
        {
            var list = new List<IBotCommand>();

            list.Add(
                new BotCommand(
                    StringID + "-play",
                    RuleGenerator.PrefixatedCommand(_prefix, "play"),
                    PlayCmd
                )
            );

            _useCommands = list;
        }

        public void Reconfigure(XElement configEl)
        {
            if (configEl == null)
                return;
            Configure(configEl);
        }

        private async Task PlayCmd(SocketMessage msg)
        {
            try
            {
#if DEBUG
                Console.WriteLine("[DEBUG][MUSIC] Entered PlayCmd.");
#endif
                if (!(_guild.Channels.FirstOrDefault(c => c is SocketVoiceChannel ac && ac.GetUser(msg.Author.Id) != null) is SocketVoiceChannel channel))
                    return;

                var audioClient = await channel.ConnectAsync().ConfigureAwait(false);
                await msg.Channel.SendMessageAsync($"Connected to {channel.Name}").ConfigureAwait(false);

                var pipeProc = StartAudioPipe(msg.Content.Split(" ")[1]);

                using var aus = audioClient.CreatePCMStream(AudioApplication.Music);

                try
                {
#if DEBUG
                    Console.WriteLine("[DEBUG][MUSIC] Starting stream copy.");
#endif
                    await pipeProc.StandardOutput.BaseStream.CopyToAsync(aus);
#if DEBUG
                    Console.WriteLine("[DEBUG][MUSIC] Stream copy ended.");
#endif
                }
                finally { await aus.FlushAsync(); }

                await Task.Run(() => pipeProc.WaitForExit(5000));

                await channel.DisconnectAsync();
#if DEBUG
                Console.WriteLine("[DEBUG][MUSIC] Left PlayCmd.");
#endif
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EXCEPT][MUSIC] {ex.Message}");
                Console.WriteLine($"[EXCEPT][MUSIC] {ex.StackTrace}");
            }
        }

        private Process StartAudioPipe(string url)
        {
            string ffmpegName = "ffmpeg";
            string terminalName = "/bin/bash";
            string youtubeDLName = "youtube-dl";
            string termSwitch = "-c";
            if (System.Runtime.InteropServices.RuntimeInformation.OSDescription.ToLower().Contains("windows"))
            {
                ffmpegName += ".exe";
                terminalName = "cmd.exe";
                youtubeDLName += ".exe";
                termSwitch = "/C";
            }

            var pipeProc = Process.Start(
                new ProcessStartInfo
                {
                    FileName = terminalName,
                    Arguments = $"{termSwitch} \"{youtubeDLName} -f 'bestaudio' {url} -o - 2>~/czvlt/debug/youtube-dl.log | {ffmpegName} -hide_banner -loglevel debug -i pipe:0 -vn -ac 2 -ar 44100 -f s16le pipe:1 2>~/czvlt/debug/ffmpeg.log\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                }
            );

            return pipeProc;
        }
    }
}
