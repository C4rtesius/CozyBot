using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Discord.WebSocket;

namespace CozyBot
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

            _configPath = Path.Combine(_guildPath, "config.xml");

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

        public void OnCtrlEvent(CtrlType type)
        {
            _ctrlEvent.Invoke(type);

            Task.Run(() => SaveConfig()).GetAwaiter().GetResult();
        }


        private void LoadConfig()
        {
#if DEBUG
            Console.WriteLine($"[DEBUG][GUILDBOT] _configPath :{_configPath}");
            Console.WriteLine($"[DEBUG][GUILDBOT] Exists(_configPath) :{File.Exists(_configPath)}");
            if (File.Exists(_configPath))
            {
                //Console.WriteLine($"[DEBUG][GUILDBOT] Contents of _configPath :{File.ReadAllText(_configPath)}");
                Console.WriteLine($"[DEBUG][GUILDBOT] Full Path :{Path.GetFullPath(_configPath)}");
            }
#endif
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
                if (modulesEl.Element("pxls-alerts") != null)
                {
                    PxlsAlertsModule pxlsModule = new PxlsAlertsModule(modulesEl, _adminIds, _guild, _guildPath);
                    _modulesDict.Add(pxlsModule.StringID, pxlsModule);
                    //pxlsModule.GuildBotConfigChanged +=
                }
                if (modulesEl.Element("music") != null)
                {
                    MusicModule musicModule = new MusicModule(modulesEl, _adminIds, _guild, _guildPath);
                    _modulesDict.Add(musicModule.StringID, musicModule);
                    //pxlsModule.GuildBotConfigChanged +=
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
                    ),
                new BotCommand(
                    "limbocmd",
                    RuleGenerator.PrefixatedCommand(_prefix, "limbo") &
                    RuleGenerator.UserByID(_superUserID),
                    LimboCommand
                    )
            };
        }
        
        private async Task LimboCommand(SocketMessage msg)
        {
            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            await _guild.DownloadUsersAsync().ConfigureAwait(false);
            var passiveDict = new Dictionary<ulong, DateTime>();
            var activeDict = new Dictionary<ulong, DateTime>();
            var timestamp = DateTime.UtcNow;
            var timeDiff = TimeSpan.FromDays(60.0d);
            var timeBoundary = timestamp - timeDiff;
            var searchChannelsList = new List<ulong>()
            {
                455231703368073228u,    // yerevan
                502949010278055947u,    // bioproblems
                594216681291644960u,    // sport-phys
                455301207486103552u,    // memes
                594216520947859472u,    // arts
                455284470577102858u,    // international
                482555295055347712u,    // science-and-it
                622677140583743488u,    // military
                518529241118408738u,    // music
                618915135469125671u,    // books
                455293744464527375u,    // anime
                455317918930960384u,    // politach
                455285679849472010u,    // gazem
                456882800474456064u,    // paradox
                455311999237095424u,    // pubg
                455314596648189963u,    // factorio
                576069178427965450u,    // ecc
                455292440794890251u,    // he
                557996950620995584u,    // fu
                455302382188756993u,    // 3d
                576069373001728010u     // ot
            };

            foreach(var user in _guild.Users)
            {
                var roleIds = user.Roles.Select(r => r.Id);
                if (roleIds.Contains(455287352701616128u)) // pxls
                    continue;
                if (roleIds.Contains(455283878223937556u)) // cz pxls infantry
                    continue;
                if (roleIds.Contains(455284945774968833u)) // unterdїd
                    continue;
                if (roleIds.Contains(594215839503220752u)) // foreigndїd
                    continue;
                if (roleIds.Contains(455284885960130560u)) // dїd
                    continue;
                if (roleIds.Contains(683422699367956481u)) // readonly
                    continue;
                if (user.IsBot)                            // bot
                    continue;
                if (user.JoinedAt.Value.UtcDateTime > timeBoundary) // if user joined after search boundary time
                    continue;
                if (roleIds.Contains(455285000330543104u)) // vault dw
                    passiveDict.Add(user.Id, timestamp);
            }

            int qs = 0;

            foreach(var chId in searchChannelsList)
            {
                Discord.IMessage lastMsg = null;
                if (_guild.GetChannel(chId) is SocketTextChannel stc)
                {
                    int q = 0;
                    var asyncMessages = stc.GetMessagesAsync();
                    var enumerator = asyncMessages.GetEnumerator();

                    try
                    {
                        while (await enumerator.MoveNext().ConfigureAwait(false))
                        {
                            foreach (var message in enumerator.Current)
                            {
                                var usrId = message.Author.Id;
                                if (passiveDict.Keys.Contains(usrId))
                                    passiveDict.Remove(usrId);
                                lastMsg = message;
                                q++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[EXCEPT][GUILDBOT] Fetch Messages failed : {stc.Name}\n{ex.Message}\n{ex.StackTrace}");
                        throw;
                    }

                    while (lastMsg.Timestamp > timeBoundary)
                    {
                        asyncMessages = stc.GetMessagesAsync(lastMsg, Discord.Direction.Before);
                        enumerator = asyncMessages.GetEnumerator();

                        try
                        {
                            while (await enumerator.MoveNext().ConfigureAwait(false))
                            {
                                foreach (var message in enumerator.Current)
                                {
                                    var usrId = message.Author.Id;
                                    if (passiveDict.Keys.Contains(usrId))
                                        passiveDict.Remove(usrId);
                                    lastMsg = message;
                                    q++;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[EXCEPT][GUILDBOT] Fetch Messages failed : {stc.Name}\n{ex.Message}\n{ex.StackTrace}");
                            throw;
                        }
                    }
                    await msg.Channel.SendMessageAsync($"Processed {q} messages in {stc.Mention}.\n{passiveDict.Keys.Count} users left.").ConfigureAwait(false);
                    qs += q;
                }
            }

//#if DEBUG
            List<string> msgStrs = new List<string>();
            var messageStr = String.Empty;
            foreach (var userId in passiveDict.Keys)
            {
                if (messageStr.Length > 1950)
                {
                    msgStrs.Add(messageStr);
                    messageStr = String.Empty;
                }
                messageStr += $"{_guild.GetUser(userId).Mention} ";
            }
            msgStrs.Add(messageStr);
            
            foreach (var str in msgStrs)
            {
                await msg.Channel.SendMessageAsync(str).ConfigureAwait(false);
            }
//#endif
            sw.Stop();
            await msg.Channel.SendMessageAsync($"Left {passiveDict.Count} users.\nProcessed {qs} messages in {sw.Elapsed.TotalSeconds} seconds.").ConfigureAwait(false);
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
            ).ConfigureAwait(false);
        }

        private async void ArchiveModule_ConfigChanged(object sender, ConfigChangedArgs args)
        {
            await SaveConfig().ConfigureAwait(false);
        }

        private async void ImageModule_ConfigChanged(object sender, ConfigChangedArgs args)
        {
            await SaveConfig().ConfigureAwait(false);
        }

        private async void CitationModule_ConfigChanged(object sender, ConfigChangedArgs args)
        {
            await SaveConfig().ConfigureAwait(false);
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
            await msg.Channel.SendMessageAsync(reply).ConfigureAwait(false);
        }

        private async Task Magic8BallCommand(SocketMessage msg)
        {
            await msg.Channel.SendMessageAsync(_magic8BallResponses[new Random(DateTime.Now.Millisecond).Next(_magic8BallResponses.Length)]).ConfigureAwait(false);
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
                    Regex diceregex = new Regex(@"^(?<dicenum>\d+)[dD](?<dicesize>\d+)$", RegexOptions.Compiled);
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

            if (size == 0 || num == 0)
            {
                return;
            }

            Random rng = new Random(DateTime.Now.Millisecond);
            for (int i = 0; i < num; i++)
            {

                result += (ulong)rng.Next(1, size);
            }

            await msg.Channel.SendMessageAsync("Ви заролили : " + result + " " + EmojiCodes.Pizdec).ConfigureAwait(false);
        }

        private async Task PingCommand(SocketMessage msg)
        {
            await msg.Channel.SendMessageAsync("Pong!").ConfigureAwait(false);
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
                    await SaveConfig().ConfigureAwait(false);
                    await msg.Channel.SendMessageAsync("Лягаю спати " + EmojiCodes.Pepe).ConfigureAwait(false);
                    //_mre.Set();
                    break;
                case "modules":
                    await ModulesCommand(words, msg).ConfigureAwait(false);
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
                        await msg.Channel.SendMessageAsync("Не підключено жодного модуля.").ConfigureAwait(false);
                        return;
                    }
                    string output = @"Список підключених модулів :" + Environment.NewLine + @"```";
                    foreach (var kvp in _modulesDict)
                    {
                        output += kvp.Key + " " + (kvp.Value.IsActive ? "включений" : "виключений") + Environment.NewLine;
                    }
                    output += @"```";

                    await msg.Channel.SendMessageAsync(output).ConfigureAwait(false);
                    break;
                case "on":
                    if (words.Length < 4)
                    {
                        return;
                    }
                    await ChangeModulesState(words, true, msg).ConfigureAwait(false);
                    break;
                case "off":
                    if (words.Length < 4)
                    {
                        return;
                    }
                    await ChangeModulesState(words, false, msg).ConfigureAwait(false);
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
                    modulesString += module + " ";
                }

                await SaveConfig().ConfigureAwait(false);

                string output = @"Наступні модулі було " + (newState ? @"увімкнено : " : @"вимкнено : ");
                output += modulesString + " " + EmojiCodes.Picardia;

                await msg.Channel.SendMessageAsync(output).ConfigureAwait(false);
            }
            else
            {
                await msg.Channel.SendMessageAsync(@"Нічого не було змінено " + EmojiCodes.Thonk).ConfigureAwait(false);
            }
        }

    }
}
