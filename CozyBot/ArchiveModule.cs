using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Timers;
using System.Xml.Linq;
using System.Threading.Tasks;
using System.Net.Http;

using Discord;
using Discord.WebSocket;


namespace CozyBot
{
    public class ArchiveModule : IGuildModule
    {
        private const int _msgLimit = 1500;

        private static string _stringID = "ArchiveModule";
        private static string _moduleXmlName = "archive";
        private static string _listFormat = 
            "`{0,-25}Timer: {1,-6}Interval: {2,-6}Image: {3,-6}Last: {4,-20}Silent: {5,-6}`" + Environment.NewLine;

        private string _workingPath;
        private int _minimumInterval =  1; // minutes = 1 min
        private int _defaultInterval = 60; // minutes = 1 hour
        private ulong _logChannelId = 0;

        private SocketGuild _guild;

        protected List<IBotCommand> _cfgCommands = new List<IBotCommand>();
        protected List<IBotCommand> _dmpCommands = new List<IBotCommand>();

        protected Dictionary<ulong, ArchiveChannelProperties> _channels = new Dictionary<ulong, ArchiveChannelProperties>();
        protected Dictionary<ulong, Timer> _timersDict = new Dictionary<ulong, Timer>();

        protected bool _isActive;

        protected string _defaultPrefix = "x!";
        protected string _prefix;

        protected List<ulong> _adminIds;

        protected XElement _configEl;
        protected XElement _moduleConfigEl;

        protected event ConfigChanged _configChanged;

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

        public bool IsActive
        {
            get
            {
                return _isActive;
            }
        }

        public string StringID { get { return _stringID; } }

        public string ModuleXmlName { get { return _moduleXmlName; } }

        public IEnumerable<IBotCommand> ActiveCommands
        {
            get
            {
                foreach (var cmd in _cfgCommands)
                {
                    yield return cmd;
                }
                foreach (var cmd in _dmpCommands)
                {
                    yield return cmd;
                }
            }
        }

        public SocketGuild Guild
        {
            get
            {
                return _guild;
            }
        }

        public ArchiveModule(XElement configEl, List<ulong> adminIds, string workingPath, SocketGuild guild)
        {
            _guild = Guard.NonNull(guild, nameof(guild));
            _configEl = Guard.NonNull(configEl, nameof(configEl));

            _adminIds = adminIds;

            if (_configEl.Element(ModuleXmlName) != null)
            {
                _moduleConfigEl = _configEl.Element(ModuleXmlName);
            }
            else
            {
                XElement moduleConfigEl =
                    new XElement(ModuleXmlName,
                        new XAttribute("on", Boolean.FalseString),
                        new XAttribute("prefix", _defaultPrefix));
                _moduleConfigEl = moduleConfigEl;

                _configEl.Add(moduleConfigEl);
            }

            _workingPath = Guard.NonNullWhitespaceEmpty(workingPath, nameof(workingPath));

            if (!Directory.Exists(_workingPath))
            {
                Directory.CreateDirectory(_workingPath);
            }
            if (!Directory.Exists(Path.Combine(_workingPath, "archive")))
            {
                Directory.CreateDirectory(Path.Combine(_workingPath, "archive"));
            }

            Configure(_configEl);
        }

        public void Reconfigure(XElement configEl)
        {
            if (configEl == null)
            {
                return;
            }

            Configure(configEl);
        }

        public void OnCtrlEvent(CtrlType type)
        {
            foreach (var timer in _timersDict.Values)
            {
                try
                {
                    timer.Stop();
                    timer.Dispose();
                }
                catch { }
            }
        }

        protected List<ulong> ExtractPermissions(XAttribute attr)
        {
            List<ulong> ids = new List<ulong>();

            if (attr != null)
            {
                string permStringValue = attr.Value;
                string[] stringIds = permStringValue.Trim().Split(" ");
                if (stringIds.Length > 0)
                {
                    for (int i = 0; i < stringIds.Length; i++)
                    {
                        if (ulong.TryParse(stringIds[i], out ulong id))
                        {
                            ids.Add(id);
                        }
                    }
                }
            }

            return ids;
        }


        protected virtual void Configure(XElement configEl)
        {
            XElement moduleCfgEl = configEl.Element(ModuleXmlName);

            bool isActive = false;

            if (moduleCfgEl.Attribute("on") != null)
            {
                if (!Boolean.TryParse(moduleCfgEl.Attribute("on").Value, out isActive))
                {
                    isActive = false;
                }
            }

            _isActive = isActive;

            string prefix = _defaultPrefix;

            if (moduleCfgEl.Attribute("prefix") != null)
            {
                if (!String.IsNullOrWhiteSpace(moduleCfgEl.Attribute("prefix").Value))
                {
                    prefix = moduleCfgEl.Attribute("prefix").Value;
                }
            }

            _prefix = prefix;

            if (moduleCfgEl.Attribute("cfgPerm") == null)
            {
                moduleCfgEl.Add(new XAttribute("cfgPerm", ""));
            }
            if (moduleCfgEl.Attribute("dmpPerm") == null)
            {
                moduleCfgEl.Add(new XAttribute("dmpPerm", ""));
            }

            List<ulong> cfgPermissionList = ExtractPermissions(moduleCfgEl.Attribute("cfgPerm"));
            List<ulong> dmpPermissionList = ExtractPermissions(moduleCfgEl.Attribute("dmpPerm"));

            // Log Channel data

            ulong logChannelId = 0;
            if (moduleCfgEl.Attribute("logchannel") != null)
            {
                _logChannelId = logChannelId;
                if (UInt64.TryParse(moduleCfgEl.Attribute("logchannel").Value, out logChannelId))
                {
                    _logChannelId = logChannelId;
                }
            }

            // Extracting channel data

            GenerateChannels(moduleCfgEl);

            if (isActive)
            {
                GenerateCfgCommands(cfgPermissionList);
                GenerateDmpCommands(dmpPermissionList);

                // Timers section
                GenerateTimers();
            }
            else
            {
                _cfgCommands = new List<IBotCommand>();
                _dmpCommands = new List<IBotCommand>();
            }
        }

        private void GenerateTimers()
        {
            foreach (var acp in _channels.Values)
            {
                if (acp.Timer)
                {
                    if (_timersDict.TryGetValue(acp.Id, out Timer timer))
                    {
                        // When timer exists in both _timers and config
                        // check and ?reconfig timer

                        if ((acp.Interval * 60000L) != (long)(timer.Interval))
                        {
                            // interval changed

                            timer.Stop();
                            timer.Interval = (acp.Interval * 60000L);

                            // run dump in between interval changes
                            Task.Run(
                                async () =>
                                {
                                    await DumpChannelById(acp.Id).ConfigureAwait(false);
                                }
                            );

                            // start timer
                            timer.Start();
                        }
                        // if interval hasn't changed nothing to do
                    }
                    else
                    {
                        // if timer exists in config but not in _timers 
                        // create and run timer

                        //config new timer
                        Timer newTimer = new Timer(acp.Interval * 60000L);
                        newTimer.AutoReset = true;
                        newTimer.Elapsed +=
                            async (o, e) =>
                            {
                                await DumpChannelById(acp.Id).ConfigureAwait(false);
                            };
                        _timersDict.Add(acp.Id, newTimer);

                        // Run dump before enabling timer
                        Task.Run(
                            async () =>
                            {
                                await DumpChannelById(acp.Id).ConfigureAwait(false);
                            }
                        );

                        // start new timer
                        newTimer.Start();
                    }
                }
                else
                {
                    if (_timersDict.TryGetValue(acp.Id, out Timer timer))
                    {
                        // When timer exists in _timers and not in config
                        // stop and destroy timer 
                        try
                        {
                            timer.Stop();
                            timer.Dispose();
                        }
                        catch { }
                        _timersDict.Remove(acp.Id);

                    }
                    // if timer does not exist anywhere - do nothing
                }
            }
        }

        private void GenerateChannels(XElement moduleCfgEl)
        {
            Dictionary<ulong, ArchiveChannelProperties> channels = new Dictionary<ulong, ArchiveChannelProperties>();
            List<XElement> channelEls = new List<XElement>(moduleCfgEl.Elements("channel"));

            ArchiveChannelProperties acp;
            foreach (XElement channel in channelEls)
            {
                acp = ExtractChannelProperties(channel);
                if (acp != null)
                {
                    channels.Add(acp.Id, acp);
                }
            }

            _channels = channels;
        }

        private ArchiveChannelProperties ExtractChannelProperties(XElement channelEl)
        {
            ulong id = 0;
            if (channelEl.Attribute("id") != null)
            {
                if (!UInt64.TryParse(channelEl.Attribute("id").Value, out id))
                {
                    return null;
                }
            }
            else
            {
                return null;
            }

            DateTime last = new DateTime(0);
            if (channelEl.Attribute("last") != null)
            {
                if (!DateTime.TryParse(channelEl.Attribute("last").Value, out last))
                {
                    channelEl.Attribute("last").Value = last.ToString("o");
                }
            }
            else
            {
                channelEl.Add(new XAttribute("last", last.ToString("o")));
            }

            last = last.ToUniversalTime();

            bool timer = false;
            if (channelEl.Attribute("timer") != null)
            {
                if (!Boolean.TryParse(channelEl.Attribute("timer").Value, out timer))
                {
                    channelEl.Attribute("timer").Value = timer.ToString();
                }
            }
            else
            {
                channelEl.Add(new XAttribute("timer", timer.ToString()));
            }

            int interval = _defaultInterval;
            if (channelEl.Attribute("interval") != null)
            {
                if (!Int32.TryParse(channelEl.Attribute("interval").Value, out interval))
                {
                    channelEl.Attribute("interval").Value = interval.ToString();
                }
                else
                {
                    if (interval < _minimumInterval)
                    {
                        interval = _minimumInterval;
                        channelEl.Attribute("interval").Value = interval.ToString();
                    }
                }
            }
            else
            {
                channelEl.Add(new XAttribute("interval", _defaultInterval));
            }

            bool image = false;
            if (channelEl.Attribute("image") != null)
            {
                if (!Boolean.TryParse(channelEl.Attribute("image").Value, out image))
                {
                    channelEl.Attribute("image").Value = image.ToString();
                }
            }
            else
            {
                channelEl.Add(new XAttribute("image", image.ToString()));
            }

            bool silent = true;
            if (channelEl.Attribute("silent") != null)
            {
                if (!Boolean.TryParse(channelEl.Attribute("silent").Value, out silent))
                {
                    channelEl.Attribute("silent").Value = silent.ToString();
                }
            }
            else
            {
                channelEl.Add(new XAttribute("silent", silent.ToString()));
            }

            string filepath;
            if (channelEl.Value != null)
            {
                if (Directory.Exists(channelEl.Value))
                {
                    filepath = channelEl.Value;
                }
                else
                {
                    filepath = Path.Combine(_workingPath, "archive", id.ToString());
                }
            }
            else
            {
                filepath = Path.Combine(_workingPath, "archive", id.ToString());
                channelEl.Value = filepath;
            }

            return new ArchiveChannelProperties(id, last, timer, interval, image, filepath, silent);
        }

        private void GenerateDmpCommands(List<ulong> dmpPermissionList)
        {
            List<ulong> allPerms = new List<ulong>(_adminIds);
            allPerms.AddRange(dmpPermissionList);
            Rule dumpRule = RuleGenerator.HasRoleByIds(allPerms)
                & RuleGenerator.PrefixatedCommand(_prefix, "dump");

            IBotCommand dmpCmd =
                new BotCommand(
                    $"{StringID}-dumpcmd",
                    dumpRule,
                    DumpCommand
                );

            _dmpCommands = new List<IBotCommand> { dmpCmd };
        }

        protected virtual void GenerateCfgCommands(List<ulong> perms)
        {
            List<ulong> allPerms = new List<ulong>(_adminIds);
            allPerms.AddRange(perms);
            Rule cmdRule = RuleGenerator.HasRoleByIds(allPerms)
                & RuleGenerator.PrefixatedCommand(_prefix, "cfg");

            IBotCommand configCmd =
                new BotCommand(
                    $"{StringID}-configcmd",
                    cmdRule,
                    ConfigCommand
                );

            _cfgCommands = new List<IBotCommand> { configCmd };
        }

        protected virtual async Task DumpCommand(SocketMessage msg)
        {
            string[] words = msg.Content.Split(" ");
            if (words.Length == 1)
            {
                if (words[0] == "x!dump")
                {
                    await DumpChannelById(msg.Channel.Id).ConfigureAwait(false);
                }
            }
            else if (words[0] == "x!dump")
            {
                if(msg.MentionedChannels.Count > 0)
                {
                    foreach(var channel in msg.MentionedChannels)
                    {
                        await DumpChannelById(channel.Id).ConfigureAwait(false);
                    }
                }
            }

        }

        private async Task DumpChannelById(ulong id)
        {
            try
            {
                if (!_channels.ContainsKey(id))
                {
                    XElement newChannel =
                        new XElement("channel",
                            new XAttribute("id", id),
                            new XAttribute("last", (new DateTime(0)).ToString("o")),
                            new XAttribute("timer", Boolean.FalseString),
                            new XAttribute("interval", _minimumInterval),
                            new XAttribute("image", Boolean.FalseString),
                            new XAttribute("silent", Boolean.FalseString),
                        _workingPath + @"archive\" + id + @"\"
                    );

                    _moduleConfigEl.Add(newChannel);

                    await RaiseConfigChanged(_configEl).ConfigureAwait(false);
                    GenerateChannels(_moduleConfigEl);

                }

                // workaround for ratelimiting
                bool success = false;
                int tries = 0;
                while (!success && (tries < 10))
                {
                    try
                    {
                        await DumpChannel(_channels[id]).ConfigureAwait(false);
                        success = true;
                    }
                    catch (Discord.Net.RateLimitedException)
                    {
                        tries++;
                        success = false;
                        await Task.Delay(10000).ConfigureAwait(false);
                    }
                }
                await RaiseConfigChanged(_configEl).ConfigureAwait(false);
                GenerateChannels(_moduleConfigEl);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
            }
        }

        private async Task DumpChannel(ArchiveChannelProperties acp)
        {
            //DateTime opStartTime = DateTime.UtcNow;
            List<IMessage> totalList = new List<IMessage>();
            List<IMessage> imageList = new List<IMessage>();

            XElement currentChanneEl = null;

            foreach (var channelEl in _moduleConfigEl.Elements("channel"))
            {
                if (channelEl.Attribute("id").Value == acp.Id.ToString())
                {
                    currentChanneEl = channelEl;
                    break;
                }
            }

            SocketGuildChannel channel = _guild.GetChannel(acp.Id);

            if (channel is SocketTextChannel)
            {
                bool boundary = false;
                IMessage boundaryMsg = null;

                var messages = await FetchMessages(channel as SocketTextChannel);

                totalList.AddRange(messages);

                if (messages.Count < 1)
                {
                    return;
                }

                foreach (var message in messages)
                {
                    if (message.Timestamp.UtcDateTime < acp.Last)
                    {
                        boundary = true;
                        break;
                    }
                    else
                    {
                        boundaryMsg = message;
                    }
                }

                while (!boundary)
                {
                    messages = await FetchMessages(channel as SocketTextChannel, boundaryMsg, Discord.Direction.Before);

                    totalList.AddRange(messages);

                    if (messages.Count < 1)
                    {
                        break;
                    }

                    foreach (var message in messages)
                    {
                        if (message.Timestamp.UtcDateTime < acp.Last)
                        {
                            boundary = true;
                            break;
                        }
                        else
                        {
                            boundaryMsg = message;
                        }
                    }
                }

                currentChanneEl.Attribute("last").Value = DateTime.UtcNow.ToString("o");

                totalList.Sort(
                    (a, b) =>
                    {
                        if (a.Timestamp == b.Timestamp) return 0;
                        else if (a.Timestamp > b.Timestamp) return 1;
                        else return -1;
                    }
                );


                Discord.IMessageChannel sendChannel = null;

                foreach (var message in totalList)
                {
                    if (message.Timestamp.UtcDateTime < acp.Last)
                        continue;
                    //if (message.Timestamp.UtcDateTime > opStartTime)
                    //    break;

                    sendChannel = message.Channel;

                    if (acp.Image)
                    {
                        if (message.Attachments.Count > 0)
                        {
                            imageList.Add(message);
                        }
                    }
                }


                int lines = await WriteMessages(totalList, acp);//, opStartTime);

                if (lines > 0)
                {
                    if (_logChannelId != 0)
                    {
                        var eba = new EmbedAuthorBuilder
                        {
                            Name = @"Shining Armor",
                            IconUrl = @"https://cdn.discordapp.com/avatars/335004246007218188/3094a7be163d3cd1d03278b53c8f08eb.png"
                        };

                        var efb = new EmbedFieldBuilder
                        {
                            IsInline = false,
                            Name = "Архівація",
                            Value = $"Збережено {lines} повідомлень у каналі {_guild.GetChannel(acp.Id).Name}"
                        };

                        var eb = new EmbedBuilder
                        {
                            Author = eba,
                            Color = Color.Green,
                            Title = "Apxів",
                            Timestamp = DateTime.Now
                        };

                        eb.Fields.Add(efb);

                        var ch = (_guild.GetChannel(_logChannelId) as SocketTextChannel);
                        await ch.SendMessageAsync(String.Empty, false, eb.Build()).ConfigureAwait(false);
                    }

                    String sendMsg = $"Збережено {lines} повідомлень. {EmojiCodes.Picardia}";

                    //if (sendChannel != msg.Channel)
                    //{
                    //    try
                    //    {

                    //    }
                    //    catch { }
                    //}
                    if (!acp.Silent)
                    {
                        try
                        {
                            await sendChannel.SendMessageAsync(sendMsg).ConfigureAwait(false);
                        }
                        catch { }
                    }
                }

                if (acp.Image)
                {
                    int images = await WriteImages(imageList, acp);

                    if (images > 0)
                    {
                        if (_logChannelId != 0)
                        {
                            var eba = new EmbedAuthorBuilder
                            {
                                Name = @"Shining Armor",
                                IconUrl = @"https://cdn.discordapp.com/avatars/335004246007218188/3094a7be163d3cd1d03278b53c8f08eb.png"
                            };

                            var efb = new EmbedFieldBuilder
                            {
                                IsInline = false,
                                Name = "Архівація",
                                Value = $"Збережено {images} зображень у каналі {_guild.GetChannel(acp.Id).Name}"
                            };

                            var eb = new EmbedBuilder
                            {
                                Author = eba,
                                Color = Color.Green,
                                Title = "Apxів",
                                Timestamp = DateTime.Now
                            };

                            eb.Fields.Add(efb);

                            var ch = (_guild.GetChannel(_logChannelId) as SocketTextChannel);
                            await ch.SendMessageAsync(String.Empty, false, eb.Build()).ConfigureAwait(false);
                        }

                        if (!acp.Silent)
                        {
                            try
                            {
                                await sendChannel.SendMessageAsync($"Збережено {images} зображень. {EmojiCodes.Picardia}").ConfigureAwait(false);
                            }
                            catch { }
                        }
                    }
                }
            }
        }

        private async Task<int> WriteMessages(List<Discord.IMessage> list, ArchiveChannelProperties acp)//, DateTime end)
        {
            string filepath = acp.Filepath;
            if (!Directory.Exists(filepath))
            {
                Directory.CreateDirectory(filepath);
            }

            int lastNumber = -1;
            string lastLog = String.Empty;
            var logs = Directory.EnumerateFiles(filepath, $"{acp.Id}-*.log");

            string tempStr;

            foreach (var log in logs)
            {
                tempStr = log.Remove(0, log.IndexOf('-') + 1);
                tempStr = tempStr.Remove(tempStr.IndexOf('.'));
                if (Int32.TryParse(tempStr, out int temp))
                {
                    if (temp > lastNumber)
                    {
                        lastNumber = temp;
                        lastLog = log;
                    }
                }
            }

            if (lastNumber == -1)
            {
                lastNumber = 0;
                lastLog = Path.Combine(filepath, $"{acp.Id}-{lastNumber}.log"); // + acp.Id + "-" + lastNumber + ".log";
                //lastLog = filepath + lastLog;
            }

            if (File.Exists(lastLog))
            {
                if ((new FileInfo(lastLog)).Length > 8388608)
                {
                    lastNumber++;
                    //lastLog = filepath + acp.Id + "-" + lastNumber + ".log";
                    lastLog = Path.Combine(filepath, $"{acp.Id}-{lastNumber}.log");
                }
            }

            //string newString;
            //string nickname;
            string timestamp;
            int lines = 0;

            using var sw = File.AppendText(lastLog);
            foreach (var message in list)
            {
                if (message.Timestamp.UtcDateTime < acp.Last)
                    continue;
                //if (message.Timestamp > end)
                //    break;
                timestamp = message.Timestamp.ToString("o"); // ???

                //nickname = message.Author.Username;
                //newString = String.Format("[{0}] : {1} : {2}", message.Timestamp, nickname, message.Content);
                    
                await sw.WriteLineAsync(
                    $"[{message.Timestamp}] : {message.Author.Username} : {message.Content}"       
                );
                    
                lines++;
            }
            return lines;
        }

        private async Task<int> WriteImages(List<Discord.IMessage> list, ArchiveChannelProperties acp)
        {
            int images = 0;

            string filepath = acp.Filepath;
            if (!Directory.Exists(filepath))
            {
                Directory.CreateDirectory(filepath);
            }

            int cores = Environment.ProcessorCount;
            int count = list.Count / cores;
            int residue = list.Count % cores;
            List<List<IMessage>> splitList = new List<List<IMessage>>();
            int curCount;
            int j = 0;
            for (int i = 0; i < cores; i++)
            {
                curCount = (i < residue) ? count + 1 : count;
                splitList.Add(list.GetRange(j, curCount));
                j += curCount;
            }

            using HttpClient hc = new HttpClient();
            
            List<Task> tasklist = new List<Task>();

            foreach (var messagesPerCore in splitList)
            {
                tasklist.Add(
                    Task.Run(
                        async () =>
                        {
                            foreach (var message in messagesPerCore)
                            {
                                int num = 0;

                                string timestamp = message.Timestamp.ToString("o").Replace(':', '-');

                                foreach (var att in message.Attachments)
                                {
                                    //string file = filepath + timestamp + "-" + num + Path.GetExtension(att.Url);
                                    string file = Path.Combine(filepath, $"{timestamp}-{num}{Path.GetExtension(att.Url)}");
                                    using FileStream fs = new FileStream(file, FileMode.Create, FileAccess.Write);
                                    var response = await hc.GetAsync(att.Url);
                                    await response.Content.CopyToAsync(fs).ConfigureAwait(false);
                                    num++;
                                    images++;
                                }
                            }
                        }
                    )
                );
            }

            await Task.WhenAll(tasklist).ConfigureAwait(false);

            return images;
        }

        private async Task<List<IMessage>> FetchMessages(SocketTextChannel textChannel)
        {
            List<IMessage> list = new List<IMessage>();

            var asyncMessages = textChannel.GetMessagesAsync();
            var enumerator = asyncMessages.GetEnumerator();
            try
            {
                while (await enumerator.MoveNext())
                {
                    foreach (var message in enumerator.Current)
                    {
                        list.Add(message);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EXCEPT][ARCHIVEMODULE] Fetch Messages failed : {textChannel.Name}");
                throw;
            }

            return list;
        }

        private async Task<List<IMessage>> FetchMessages(SocketTextChannel textChannel, IMessage from, Discord.Direction direction)
        {
            List<IMessage> list = new List<IMessage>();

            var asyncMessages = textChannel.GetMessagesAsync(from, direction);
            var enumerator = asyncMessages.GetEnumerator();

            // 23.04.2019
            // There exists a bug (?!) when enumerator stumbles upon 
            // osu! spectator invite (finished ?), it throws an exception and
            // everything breaks.
            // Cannot/do not want to reproduce it atm.
            // 24.09.2019
            // No idea if that bug still persists.
            // TODO : looks like this section needs rewriting/refactoring

            try
            {
                while (await enumerator.MoveNext())
                {
                    foreach (var message in enumerator.Current)
                    {
                        list.Add(message);
                    }
                }
            }
            catch (Exception ex)
            {
                //Console.WriteLine(textChannel.Name);
                throw ex;
            }
            return list;
        }

        protected virtual async Task ConfigCommand(SocketMessage msg)
        {
            string content = msg.Content;
            string[] words = content.Split(" ");

            switch (words[1])
            {
                case "list":
                    List<string> outputMsgs = new List<string>();

                    string output = String.Empty;
                    foreach (var kvp in _channels)
                    {
                        if (output.Length > _msgLimit)
                        {
                            outputMsgs.Add(output);
                            output = String.Empty;
                        }

                        if (_guild.GetChannel(kvp.Key) != null)
                        {
                            output += String.Format(
                                _listFormat,
                                _guild.GetChannel(kvp.Key).Name,
                                kvp.Value.Timer,
                                kvp.Value.Interval,
                                kvp.Value.Image,
                                kvp.Value.Last,
                                kvp.Value.Silent
                            );
                        }
                    }

                    outputMsgs.Add(output);

                    foreach (var outputMsg in outputMsgs)
                    {
                        await msg.Channel.SendMessageAsync(outputMsg).ConfigureAwait(false);
                    }
                    return;
                case "perm" when words.Length > 3:
                    await PermissionControlCommand(words[2], msg).ConfigureAwait(false);
                    return;
                case "setlog":
                    await SetLogCommand(words[2], msg).ConfigureAwait(false);
                    await msg.Channel.SendMessageAsync($"Налаштування було змінено {EmojiCodes.Picardia}").ConfigureAwait(false);
                    return;
                default:
                    if (msg.MentionedChannels.Count <= 0)
                    {
                        return;
                    }
                    break;
            };

            List<ulong> existingChannels = new List<ulong>();

            foreach (var channelEl in _moduleConfigEl.Elements("channel"))
            {
                if (channelEl.Attribute("id") != null)
                {
                    if (UInt64.TryParse(channelEl.Attribute("id").Value, out ulong id))
                    {
                        existingChannels.Add(id);
                    }
                }
            }

            foreach (var mentionedChannel in msg.MentionedChannels)
            {
                XElement currentChannelEl = null;
                if (existingChannels.Contains(mentionedChannel.Id))
                {
                    foreach (var channelEl in _moduleConfigEl.Elements("channel"))
                    {
                        if (UInt64.Parse(channelEl.Attribute("id").Value) == mentionedChannel.Id)
                        {
                            currentChannelEl = channelEl;
                            break;
                        }
                    }
                }
                else
                {
                    currentChannelEl =
                        new XElement("channel",
                            new XAttribute("id", mentionedChannel.Id),
                            new XAttribute("last", (new DateTime(0)).ToString("o")),
                            new XAttribute("timer", false.ToString()),
                            new XAttribute("interval", _minimumInterval),
                            new XAttribute("image", false.ToString()),
                            new XAttribute("silent", true.ToString()),
                            $@"{_workingPath}archive\{mentionedChannel.Id}\"
                        );
                    _moduleConfigEl.Add(currentChannelEl);
                }

                foreach (var word in words)
                {
                    var _ = word switch
                    {
                        "+timer"  => currentChannelEl.Attribute("timer" ).Value = Boolean. TrueString,
                        "-timer"  => currentChannelEl.Attribute("timer" ).Value = Boolean.FalseString,
                        "+img"    => currentChannelEl.Attribute("image" ).Value = Boolean. TrueString,
                        "-img"    => currentChannelEl.Attribute("image" ).Value = Boolean.FalseString,
                        "+silent" => currentChannelEl.Attribute("silent").Value = Boolean. TrueString,
                        "-silent" => currentChannelEl.Attribute("silent").Value = Boolean.FalseString,
                        _ => String.Empty
                    };
                    if (Int32.TryParse(word, out int interval))
                    {
                        if (interval >= _minimumInterval)
                        {
                            currentChannelEl.Attribute("interval").Value = interval.ToString();
                        }
                    }
                }
               
            }

            await RaiseConfigChanged(_configEl).ConfigureAwait(false);

            GenerateChannels(_moduleConfigEl);
            GenerateTimers();

            await msg.Channel.SendMessageAsync($"Налаштування було змінено {EmojiCodes.Picardia}").ConfigureAwait(false);
        }

        protected virtual async Task SetLogCommand(string channelStr, SocketMessage msg)
        {
            var channels = msg.MentionedChannels;
            SocketGuildChannel logChannel = null;
            foreach (var channel in channels)
            {
                logChannel = channel;
                break;
            }
            XAttribute logChannelAttr = null;
            if (_logChannelId != logChannel.Id)
            {
                _logChannelId = logChannel.Id;

                if (_moduleConfigEl.Attribute("logchannel") != null)
                {
                    logChannelAttr = _moduleConfigEl.Attribute("logchannel");
                }
                else
                {
                    logChannelAttr = new XAttribute("logchannel", String.Empty);
                    _moduleConfigEl.Add(logChannelAttr);
                }
                logChannelAttr.Value = _logChannelId.ToString();

                await RaiseConfigChanged(_configEl).ConfigureAwait(false);
            }
        }

        protected virtual async Task PermissionControlCommand(string category, SocketMessage msg)
        {
            var roles = msg.MentionedRoles;
            List<ulong> rolesIds = new List<ulong>();

            foreach (var role in roles)
            {
                rolesIds.Add(role.Id);
            }

            switch (category)
            {
                case "cfg":
                    await ModifyPermissions(_moduleConfigEl.Attribute("cfgPerm"), rolesIds).ConfigureAwait(false);
                    GenerateCfgCommands(rolesIds);
                    break;
                case "dump":
                    await ModifyPermissions(_moduleConfigEl.Attribute("dmpPerm"), rolesIds).ConfigureAwait(false);
                    GenerateDmpCommands(rolesIds);
                    break;
                default:
                    break;
            }

            await msg.Channel.SendMessageAsync($"Дозволи було змінено {EmojiCodes.Picardia}").ConfigureAwait(false);
        }

        protected virtual async Task ModifyPermissions(XAttribute attr, List<ulong> ids)
        {
            string newValue = String.Empty;

            foreach (var id in ids)
            {
                newValue += $"{id} ";
            }

            attr.Value = newValue;

            await RaiseConfigChanged(_configEl).ConfigureAwait(false);
        }

        protected async Task RaiseConfigChanged(XElement configEl)
        {
            await Task.Run(
                () =>
                {
                    _configChanged(this, new ConfigChangedArgs(configEl));
                }
            );
        }
    }
}
