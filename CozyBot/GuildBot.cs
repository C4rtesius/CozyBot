using System;
using System.IO;
using System.Text;
using System.Xml.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Discord.WebSocket;

namespace DiscordBot1
{
    public class GuildBot : IBot
    {
        private static string[] _magic8BallResponses =
        {
            "Це безсумнівно " + EmojiCodes.Picardia,
            "Це безперечно так " + EmojiCodes.Picardia,
            "Без сумнівів " + EmojiCodes.Picardia,
            "Да, абсолютно " + EmojiCodes.Picardia,
            "Можете покладатись на це " + EmojiCodes.Picardia,
            "Наскільки я бачу - так " + EmojiCodes.Picardia,
            "Скоріш за все " + EmojiCodes.Picardia,
            "Так і є " + EmojiCodes.Picardia,
            "Так " + EmojiCodes.Picardia,
            "Все вказує на це " + EmojiCodes.Picardia,
            "Відповідь туманна, спробуйте ще " + EmojiCodes.Thonk,
            "Спитайте пізніше " + EmojiCodes.Thonk,
            "Краще не відповідатиму зараз " + EmojiCodes.Thonk,
            "Не можу передбачити " + EmojiCodes.Thonk,
            "Сконцентруйтесь та спитайте знову " + EmojiCodes.Thonk,
            "Не розраховуйте на це " + EmojiCodes.BobRoss,
            "Я відповім - ні " + EmojiCodes.BobRoss,
            "Мої джерела кажуть - ні " + EmojiCodes.BobRoss,
            "Виглядає погано " + EmojiCodes.BobRoss,
            "Дуже навряд " + EmojiCodes.BobRoss
        };

        private Dictionary<string, IBotModule> _modulesDict;
        private List<IBotCommand> _commandsList;
        private XDocument _config;
        private string _guildPath;
        private string _configPath;
        private string _prefix;
        private List<ulong> _adminIds;
        private object _configLockObject = new object();

        private ulong _clientId;
        private ulong _superUserID = 301809460803010560UL;

        private event CtrlEventHandler _ctrlEvent;

        protected event CtrlEventHandler CtrlEvent
        {
            add
            {
                if (value != null)
                {
                    _ctrlEvent += value;
                }
            }
            remove
            {
                if (value != null)
                {
                    _ctrlEvent -= value;
                }
            }
        }

        protected SocketGuild _guild;

        public Dictionary<string, IBotModule> ModulesDict
        {
            get
            {
                return _modulesDict;
            }
        }

        public GuildBot(SocketGuild guild, string guildPath, ulong clientId)
        {
            _guild = guild ?? throw new NullReferenceException("Guild cannot be null!");

            _guildPath = guildPath ?? throw new ArgumentNullException("configPath cannot be null");

            _configPath = _guildPath + "config.xml";

            _clientId = clientId;

            if (!File.Exists(_configPath))
            {
                throw new ApplicationException("Invalid configPath, file does not exist.");
            }

            _adminIds = new List<ulong>();

            LoadConfig();

            foreach (var role in guild.Roles)
            {
                if (role.Permissions.Administrator)
                {
                    _adminIds.Add(role.Id);
                }
            }

            _modulesDict = new Dictionary<string, IBotModule>();

            LoadDefaultCommands();

            LoadModules();
        }

        public void Dispatch(SocketMessage msg)
        {
            foreach (var module in _modulesDict.Values)
            {
                foreach (var cmd in module.ActiveCommands)
                {
                    if (cmd.CanExecute(msg))
                    {
                        cmd.ExecuteCommand(msg);
                        return;
                    }
                }
            }
            foreach (var cmd in _commandsList)
            {
                if (cmd.CanExecute(msg))
                {
                    cmd.ExecuteCommand(msg);
                }
            }
        }

        public async void OnCtrlEvent(CtrlType type)
        {
            _ctrlEvent.Invoke(type);

            await SaveConfig();
        }


        private void LoadConfig()
        {
            using (var stream = File.Open(_configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                _config = XDocument.Load(stream);
            }

            if (_config.Root.Name != "guildbotconfig")
            {
                throw new ApplicationException("Configuration file not found.");
            }

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

        private void LoadModules()
        {
            var modulesEl = _config.Root.Element("modules");
            if (modulesEl != null)
            {
                if (modulesEl.Element("usercite") != null)
                {
                    CitationModule citationModule = new CitationModule(modulesEl, _adminIds, _clientId, _guildPath);
                    _modulesDict.Add(citationModule.StringID, citationModule);
                    citationModule.GuildBotConfigChanged += CitationModule_ConfigChanged;
                }

                if (modulesEl.Element("userimg") != null)
                {
                    ImageModule imageModule = new ImageModule(modulesEl, _adminIds, _clientId, _guildPath);
                    _modulesDict.Add(imageModule.StringID, imageModule);
                    imageModule.GuildBotConfigChanged += ImageModule_ConfigChanged;
                }

                if (modulesEl.Element("archive") != null)
                {
                    ArchiveModule archiveModule = new ArchiveModule(modulesEl, _adminIds, _guildPath, _guild);
                    _modulesDict.Add(archiveModule.StringID, archiveModule);
                    archiveModule.GuildBotConfigChanged += ArchiveModule_ConfigChanged;
                    CtrlEvent += archiveModule.OnCtrlEvent;
                }
            }
        }


        private void LoadDefaultCommands()
        {
            _commandsList = new List<IBotCommand>()
            {
                new BotCommand(
                    "pingcmd",
                    RuleGenerator.PrefixatedCommand(_prefix, "ping"),
                    PingCommand),
                new BotCommand(
                    "ctrlcmd",
                    RuleGenerator.PrefixatedCommand(_prefix, "ctrl") &
                    RuleGenerator.UserByID(_superUserID),
                    CtrlCommand
                    ),
                new BotCommand(
                    "rollcmd",
                    RuleGenerator.PrefixatedCommand(_prefix, "roll"),
                    RollCommand
                    ),
                new BotCommand(
                    "yesnocmd",
                    RuleGenerator.PrefixatedCommand(_prefix, "yesno"),
                    BinaryChoiceCommand
                    ),
                new BotCommand(
                    "magic8ballcmd",
                    RuleGenerator.PrefixatedCommand(_prefix, "8ball"),
                    Magic8BallCommand
                    )
            };
        }


        private async Task SaveConfig()
        {
            await Task.Run(
                () => {
                    lock (_configLockObject)
                    {
                       _config.Save(_configPath);
                    }
                }

            );
        }

        private async void ArchiveModule_ConfigChanged(object sender, ConfigChangedArgs args)
        {
            await SaveConfig();
        }

        private async void ImageModule_ConfigChanged(object sender, ConfigChangedArgs args)
        {
            await SaveConfig();
        }

        private async void CitationModule_ConfigChanged(object sender, ConfigChangedArgs args)
        {
            await SaveConfig();
        }

        private async Task BinaryChoiceCommand(SocketMessage msg)
        {
            Random rng = new Random(DateTime.Now.Millisecond);
            int res = rng.Next(10);
            string reply = String.Empty;
            if (res > 4)
            {
                reply += "Так " + EmojiCodes.Pizdec;
            }
            else
            {
                reply += "Ні " + EmojiCodes.Pizdec;
            }
            await msg.Channel.SendMessageAsync(reply);
        }

        private async Task Magic8BallCommand(SocketMessage msg)
        {
            await msg.Channel.SendMessageAsync(_magic8BallResponses[new Random(DateTime.Now.Millisecond).Next(_magic8BallResponses.Length)]);
        }

        private async Task RollCommand(SocketMessage msg)
        {
            var words = msg.Content.Split(" ");
            int num = 1;
            int size = 6;
            ulong result = 0;

            switch (words.Length)
            {
                case 1:
                    break;
                case 2:
                    Regex diceregex = new Regex(@"^(?<dicenum>\d)[dD](?<dicesize>\d)$", RegexOptions.Compiled);
                    var dicematch = diceregex.Match(words[1]);

                    if (dicematch != Match.Empty)
                    {
                        var diceGroups = dicematch.Groups;

                        if (Int32.TryParse(diceGroups["dicenum"].Value, out int diceNumR))
                        {
                            num = diceNumR;
                            if (Int32.TryParse(diceGroups["dicesize"].Value, out int diceSizeR))
                            {
                                size = diceSizeR;
                            }
                            else
                            {
                                return;
                            }
                        }
                        else
                        {
                            return;
                        }
                    }
                    else if (Int32.TryParse(words[1], out int diceSizeI))
                    {
                        size = diceSizeI;
                    }
                    else
                    {
                        return;
                    }
                    break;
                case 3:
                    if (Int32.TryParse(words[1], out int diceNum))
                    {
                        num = diceNum;

                        if (Int32.TryParse(words[2], out int diceSize))
                        {
                            size = diceSize;
                        }
                        else
                        {
                            return;
                        }
                    }
                    else
                    {
                        return;
                    }

                    break;
                default:
                    return;
            }

            if (size > Int32.MaxValue || num > Int32.MaxValue)
            {
                return;
            }

            Random rng = new Random(DateTime.Now.Millisecond);
            for (int i = 0; i < num; i++)
            {
                result += (ulong)rng.Next(1, size);
            }

            await msg.Channel.SendMessageAsync("Ви заролили : " + result + " " + EmojiCodes.Pizdec);
        }

        private async Task PingCommand(SocketMessage msg)
        {
            await msg.Channel.SendMessageAsync("Pong!");
        }

        private async Task CtrlCommand(SocketMessage msg)
        {
            string msgContent = msg.Content;
            string[] words = msgContent.Split(" ");

            if (words.Length < 2)
            {
                return;
            }

            switch (words[1])
            {
                case "off":
                    await SaveConfig();
                    await msg.Channel.SendMessageAsync("Лягаю спати " + EmojiCodes.Pepe);
                    //_mre.Set();
                    break;
                case "modules":
                    await ModulesCommand(words, msg);
                    break;
                default:
                    break;
            }
        }

        private async Task ModulesCommand(string[] words, SocketMessage msg)
        {
            if (words.Length < 3)
            {
                return;
            }

            switch (words[2])
            {
                case "list":
                    if (_modulesDict.Count == 0)
                    {
                        await msg.Channel.SendMessageAsync("Не підключено жодного модуля.");
                        return;
                    }
                    string output = @"Список підключених модулів :" + Environment.NewLine + @"```";
                    foreach (var kvp in _modulesDict)
                    {
                        output += kvp.Key + " " + (kvp.Value.IsActive ? "включений" : "виключений") + Environment.NewLine;
                    }
                    output += @"```";

                    await msg.Channel.SendMessageAsync(output);
                    break;
                case "on":
                    if (words.Length < 4)
                    {
                        return;
                    }
                    await ChangeModulesState(words, true, msg);
                    break;
                case "off":
                    if (words.Length < 4)
                    {
                        return;
                    }
                    await ChangeModulesState(words, false, msg);
                    break;
                default:
                    break;
            }
        }

        private async Task ChangeModulesState(string[] words, bool newState, SocketMessage msg)
        {
            List<string> modulesToProcess = new List<string>();

            for (int i = 3; i < words.Length; i++)
            {
                modulesToProcess.Add(words[i]);
            }

            IEnumerable<XElement> modulesElList = _config.Root.Element("modules").Elements();

            List<string> modulesProcessed = new List<string>();

            foreach (var moduleStringID in _modulesDict.Keys)
            {
                if (modulesToProcess.Contains(moduleStringID))
                {
                    foreach (var moduleEl in modulesElList)
                    {
                        if (moduleEl.Name.ToString() == _modulesDict[moduleStringID].ModuleXmlName)
                        {
                            if (Boolean.TryParse(moduleEl.Attribute("on").Value, out bool state))
                            {
                                if (state == newState)
                                {
                                    continue;
                                }
                                moduleEl.Attribute("on").Value = newState.ToString();
                                modulesProcessed.Add(moduleStringID);
                            }
                        }
                    }
                }
            }

            if (modulesProcessed.Count > 0)
            {
                string modulesString = String.Empty;

                foreach (var module in modulesProcessed)
                {
                    _modulesDict[module].Reconfigure(_config.Root.Element("modules"));
                    modulesString += module;
                }

                await SaveConfig();

                string output = @"Наступні модулі було " + (newState ? @"увімкнено : " : @"вимкнено : ");
                output += modulesString + " " + EmojiCodes.Picardia;

                await msg.Channel.SendMessageAsync(output);
            }
            else
            {
                await msg.Channel.SendMessageAsync(@"Нічого не було змінено " + EmojiCodes.Thonk);
            }
        }

    }
}
