using System;
using System.IO;
using System.Text;
using System.Xml.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

using Discord;
using Discord.WebSocket;

namespace CozyBot
{
    /// <summary>
    /// ContentModule specialization - works with citations.
    /// </summary>
    public class CitationModule : ContentModule
    {
        //Private Fields

        /// <summary>
        /// Used for Discord message limit check.
        /// </summary>
        private static int _msgLengthLimit = 1800;

        /// <summary>
        /// Filename of module config.
        /// </summary>
        private static string _configFileName = "CitationModuleConfig.xml";

        /// <summary>
        /// String module Identifier.
        /// </summary>
        private static string _stringID = "CitationModule";

        /// <summary>
        /// Module name in Guild XML config.
        /// </summary>
        private static string _moduleXmlName = "usercite";

        /// <summary>
        /// Module working folder.
        /// </summary>
        private static string _moduleFolder = @"usercite";

        /// <summary>
        /// String for citation usage count in XML.
        /// </summary>
        private static string _usageCountAttributeName = "used";

        /// <summary>
        /// Module XML config path.
        /// </summary>
        protected string _moduleConfigFilePath = String.Empty;

        /// <summary>
        /// Regex used in Add command parsing.
        /// </summary>
        private string _addCommandRegex = @"^(?<pref>\S+)\s+(?<key>\S+)\s+(?<content>[\s\S]+)$";

        /// <summary>
        /// ConcurrentDictionary to implement ratelimiting per user, per channel, per command key.
        /// </summary>
        private ConcurrentDictionary<string, Task> _ratelimitDict;

        /// <summary>
        /// Forbidden keys (because they are valid commands).
        /// </summary>
        protected string[] _blacklistedKeys = new string[]
        {
            "add",
            "list",
            "vlist",
            "help",
            "cfg",
            "del"
        };

        /// <summary>
        /// Regex used in Add command parsing.
        /// </summary>
        protected override string AddCommandRegex => _addCommandRegex;

        // Public Properties

        /// <summary>
        /// String module identifier.
        /// </summary>
        public override string StringID => _stringID;

        /// <summary>
        /// Module name in Guild XML config.
        /// </summary>
        public override string ModuleXmlName => _moduleXmlName;

        /// <summary>
        /// Module XML config path.
        /// </summary>
        public override string ModuleConfigFilePath
            => (_moduleConfigFilePath == String.Empty) 
              ? _moduleConfigFilePath = Path.Combine(_guildPath, _configFileName) 
              : _moduleConfigFilePath;

        /// <summary>
        /// Citation module constructor.
        /// </summary>
        /// <param name="configEl">XML Element containing Guild modules config.</param>
        /// <param name="adminIds">IDs of Guild admins.</param>
        /// <param name="clientId">Bot ID.</param>
        /// <param name="workingPath">Path to module working folder.</param>
        public CitationModule(XElement configEl, List<ulong> adminIds, ulong clientId, string workingPath)
            : base (configEl, adminIds, clientId, workingPath)
        {
            if (!Directory.Exists(Path.Combine(_guildPath, _moduleFolder)))
            {
                Directory.CreateDirectory(Path.Combine(_guildPath, _moduleFolder));
            }

            _ratelimitDict = new ConcurrentDictionary<string, Task>();
        }

        /// <summary>
        /// Generates citations posting commands using specified citation key.
        /// </summary>
        /// <param name="key">Key associated with citation category.</param>
        /// <returns>Function performing citation access associated with specified key.</returns>
        protected override Func<SocketMessage, Task> UseCommandGenerator(string key)
        {
            return async (msg) =>
            {
                try
                {
                    await msg.DeleteAsync().ConfigureAwait(false);
                }
                catch
                {
                    // TODO: add logging or specify concrete Exception (?)
                }

                string dictKey = $"{msg.Author.Id}{msg.Channel.Id}{key}";
                if (_ratelimitDict.ContainsKey(dictKey))
                    return;
                else
                    _ratelimitDict.TryAdd(dictKey, Task.Run(() => { Thread.Sleep(10000); _ratelimitDict.TryRemove(dictKey, out _); }));

                var citationsList = GetItemsListByKey(key);
                if (citationsList.Count == 0)
                {
                    return;
                }
                
                XElement citationEl = citationsList[_rnd.Next() % citationsList.Count];
                string citationFileName = citationEl.Value;

                string citation = String.Empty;
                try
                {
                    citation = File.ReadAllText(Path.Combine(_guildPath, _moduleFolder, citationFileName));
                }
                catch
                {
                    return;
                }

                if (citation.Length > _msgLengthLimit)
                {
                    // TODO : implement multimessage citations (however unlikely, because no chance to add message
                    // longer than limit.) Just placeholder to prevent invalid operations.
                    return;
                }

                try
                {
                    await SendTextMessageHumanLike(msg, citation).ConfigureAwait(false);
                }
                catch
                {
                    return;
                    // TODO : implement more concise exception handling (?)
                }

                // Incrementing usage count.

                if (citationEl.Attribute(_usageCountAttributeName) != null)
                {
                    if (Int32.TryParse(citationEl.Attribute(_usageCountAttributeName).Value, out int uses))
                    {
                        uses++;
                        citationEl.Attribute(_usageCountAttributeName).Value = $"{uses}";
                    }
                    else
                    {
                        citationEl.Attribute(_usageCountAttributeName).Value = $"{1}";
                    }
                }
                else
                {
                    citationEl.Add(
                        new XAttribute(
                            _usageCountAttributeName, $"{1}"
                        )
                    );
                }

                await ModuleConfigChanged().ConfigureAwait(false);
                Reconfigure(_configEl);
            };
        }

        /// <summary>
        /// Method to send messages in human-like fashion. Just for lulz.
        /// </summary>
        /// <param name="msg">SocketMessage which triggered action.</param>
        /// <param name="line">String to send.</param>
        /// <returns>Async Task performing sending message in human-like fashion.</returns>
        private async Task SendTextMessageHumanLike(SocketMessage msg, string line)
        {
            await Task.Delay(1500 + (_rnd.Next() % 2000)).ConfigureAwait(false);
            await msg.Channel.TriggerTypingAsync().ConfigureAwait(false);

            // Making delay dependent on citation length
            // further testing needed
            // 25.09.2019 
            // added advanced delay mimicking continuonus typing
            int totalDelay = 500 + line.Length * 50 + (_rnd.Next() % 2000);
            for (; totalDelay > 5000; totalDelay -= 5000)
            {
                await Task.Delay(5000).ConfigureAwait(false);
                await msg.Channel.TriggerTypingAsync().ConfigureAwait(false);
            }
            await Task.Delay(totalDelay).ConfigureAwait(false);

            //await Task.Delay(3000 + (_rnd.Next() % 4000));
            await msg.Channel.SendMessageAsync(line).ConfigureAwait(false);
        }

        /// <summary>
        /// Command performing citation adition logic.
        /// </summary>
        /// <param name="msg">Message which invoked this command, containing citation to save.</param>
        /// <returns>Async Task perofrming citation addition logic.</returns>
        protected override async Task AddCommand(SocketMessage msg)
        {
            try
            {
                await msg.DeleteAsync().ConfigureAwait(false);
            }
            catch
            {
                // TODO: add logging or specify concrete Exception (?)
            }

            if (!Regex.IsMatch(msg.Content, AddCommandRegex))
            {
                return;
            }

            var regexMatch = Regex.Match(msg.Content, AddCommandRegex);

            string[] keys = regexMatch.Groups["key"].Value.Split('.');
            
            // check for blacklisted keys
            foreach (var blKey in _blacklistedKeys)
            {
                if (String.Compare(keys[0], blKey) == 0)
                {
                    return;
                }
            }

            Guid newItemGuid = Guid.NewGuid();

            string newItemFileName = newItemGuid.ToString() + ".dat";

            XElement newItem = 
                new XElement(
                    "item",
                    new XAttribute("name", newItemGuid.ToString()),
                    newItemFileName
                );

            try
            {
                File.WriteAllText(Path.Combine(_guildPath, _moduleFolder, newItemFileName), regexMatch.Groups["content"].Value);
            }
            catch //(Exception ex)
            {
                return;
            }


            XElement currentEl = _moduleConfig.Root;

            for (int i = 0; i < keys.Length; i++)
            {
                XElement newEl = null;
                foreach (var el in currentEl.Elements("key"))
                {
                    if (el.Attribute("name") != null)
                    {
                        if (String.Compare(el.Attribute("name").Value, keys[i]) == 0)
                        {
                            newEl = el;
                            break;
                        }
                    }
                }
                if (newEl == null)
                {
                    newEl =
                        new XElement(
                            "key",
                            new XAttribute("name", keys[i])
                        );
                    currentEl.Add(newEl);
                }
                currentEl = newEl;
            }

            currentEl.Add(newItem);

            try
            {
                await ModuleConfigChanged().ConfigureAwait(false);
            }
            catch
            {
                // TODO : Implement
            }

            Reconfigure(_configEl);

            await msg.Channel.SendMessageAsync($"Записав цитатку {EmojiCodes.DankPepe}").ConfigureAwait(false);
        }

        protected override void GenerateUseCommands(List<ulong> perms)
        {
            // Generate base use commands from ContentModule
            base.GenerateUseCommands(perms);

            // Add verbose listing command

            List<ulong> allListPerms = new List<ulong>(_adminIds);
            allListPerms.AddRange(perms);
            Rule listRule = RuleGenerator.HasRoleByIds(allListPerms)
                & RuleGenerator.PrefixatedCommand(_prefix, "vlist");

            _useCommands.Add(
                new BotCommand(
                    $"{StringID}-listcmd",
                    listRule,
                    VerboseListCommand
                )
            );
        }

        protected override async Task ListCommand(SocketMessage msg)
        {
            try
            {
                await msg.DeleteAsync().ConfigureAwait(false);
            }
            catch
            {
                // TODO : add proper exception handling
            }

            // TODO : fix `c!list key` when key contains only 1 item

            if (!Regex.IsMatch(msg.Content, ListCommandRegex))
            {
                return;
            }
            var regexMatch = Regex.Match(msg.Content, ListCommandRegex);

            string keyStr = regexMatch.Groups["key"].Value;
            var listRoot = GetRootByKey(keyStr);

            List<string> outputMsgs = new List<string>();

            //string output = @"**Список доступних цитат" 
            //    + ((String.IsNullOrWhiteSpace(keyStr) ? "" : @" по підключу `" + keyStr + @"`")) 
            //    + @" :**" + Environment.NewLine + @"```";

            string output =
                $@"**Список доступних цитат" +
                $@"{(String.IsNullOrWhiteSpace(keyStr) ? "" : @$" за ключем `{keyStr}`")} :** {Environment.NewLine}```";

            var list = 
                RPKeyListGenerator
                (
                    listRoot,
                    String.IsNullOrWhiteSpace(keyStr) ? "" : keyStr + ".",
                    false
                );
            if (list.Count == 0)
            {
                return;
            }
            list.Add(keyStr);
            foreach (var key in list)
            {
                if (output.Length + key.Length < _msgLengthLimit)
                {
                    output += Environment.NewLine + key;
                }
                else
                {
                    output += @"```";
                    outputMsgs.Add(output);
                    output = @"```" + key;
                }
            }

            output += @"```";
            outputMsgs.Add(output);

            var ch = await msg.Author.GetOrCreateDMChannelAsync();

            foreach (var outputMsg in outputMsgs)
            {
                await ch.SendMessageAsync(outputMsg).ConfigureAwait(false);
            }

            try
            {
                await msg.Channel.SendMessageAsync($"{msg.Author.Mention} подивись в приватні повідомлення {EmojiCodes.Bumagi}").ConfigureAwait(false);
            }
            catch
            {
                //
            }
        }

        protected virtual async Task VerboseListCommand(SocketMessage msg)
        {
            try
            {
                await msg.DeleteAsync().ConfigureAwait(false);
            }
            catch
            {
                // TODO: add logging or specify concrete Exception (?)
            }

            if (!Regex.IsMatch(msg.Content, ListCommandRegex))
            {
                return;
            }

            var regexMatch = Regex.Match(msg.Content, ListCommandRegex);
            var cmdKey = regexMatch.Groups["key"].Value;

            var listRoot = GetRootByKey(cmdKey);
            var itemsDict = new Dictionary<string, XElement>();

            RPItemDictGenerator(
                listRoot, 
                String.IsNullOrWhiteSpace(cmdKey) ? "" : cmdKey + ".", 
                itemsDict
            );

            var ch = await msg.Author.GetOrCreateDMChannelAsync();

            List<string> outputMsgs = new List<string>();

            string output = 
                $@"**Розширений список цитат " +
                $@"{(String.IsNullOrWhiteSpace(cmdKey) ? "" : @$" за ключем `{cmdKey}`")} :** {Environment.NewLine}";

            string citation = String.Empty;
            string keyStr = String.Empty;

            foreach (var kvp in itemsDict)
            {
                string citationFileName = kvp.Value.Value;

                try
                {
                    citation = File.ReadAllText(Path.Combine(_guildPath, _moduleFolder, citationFileName));
                }
                catch
                {
                    continue;
                }
                if (citation.Length > _msgLengthLimit) 
                {
                    // if file contents are longer than limit then skip file
                    continue;
                }
                keyStr = $@"`{kvp.Key}` :";
                if (output.Length + keyStr.Length + citation.Length < _msgLengthLimit)
                {
                    output = $"{output}{keyStr}{Environment.NewLine}{citation}{Environment.NewLine}";
                    //output += Environment.NewLine + keyStr;
                    //output += Environment.NewLine + citation;
                }
                else
                {
                    outputMsgs.Add(output);
                    output = keyStr + Environment.NewLine + citation;
                }
            }

            outputMsgs.Add(output);

            foreach (var outputMsg in outputMsgs)
            {
                await ch.SendMessageAsync(outputMsg).ConfigureAwait(false);
            }

            try
            {
                await msg.Channel.SendMessageAsync(
                    $"{msg.Author.Mention} подивись в приватні повідомлення {EmojiCodes.Bumagi}");
            }
            catch
            {
                //
            }
        }

        protected override async Task HelpCommand(SocketMessage msg)
        {
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
                    Name = guild.GetUser(_clientId).Username,
                    IconUrl = guild.GetUser(_clientId).GetAvatarUrl()
            };

                var efb = new EmbedFieldBuilder
                {
                    IsInline = false,
                    Name = "Команди цитатного модуля",
                    Value =
                    _prefix + @"cfg perm [use/add/del/cfg] @Роль1 @Роль2 ... - виставлення прав доступу до команд" + Environment.NewLine +
                    _prefix + @"add автор цитата - записати у файл автора цитату" + Environment.NewLine +
                    _prefix + @"del автор - видалити цитати автора" + Environment.NewLine +
                    _prefix + @"list - отримати список доступних авторів у Приватних Повідомленнях" + Environment.NewLine +
                    _prefix + @"автор - отримати цитату автора" + Environment.NewLine +
                    _prefix + @"help - цей список команд"
                };

                var efob = new EmbedFooterBuilder
                {
                    Text = "Пора оффать чат."
                };

                var eb = new EmbedBuilder
                {
                    Author = eba,
                    Color = Color.Green,
                    ThumbnailUrl = iconUrl,
                    Title = "Довідка :",
                    Timestamp = DateTime.Now,
                    Footer = efob
                };

                eb.Fields.Add(efb);

                var dm = await msg.Author.GetOrCreateDMChannelAsync();

                await dm.SendMessageAsync(String.Empty, false, eb.Build()).ConfigureAwait(false);

                await msg.Channel.SendMessageAsync(
                    $"{msg.Author.Mention} подивись в приватні повідомлення {EmojiCodes.Bumagi}");
            }
        }

        protected override async Task DeleteCommand(SocketMessage msg)
        {
            if (!Regex.IsMatch(msg.Content, ListCommandRegex))
            {
                return;
            }
            var regexMatch = Regex.Match(msg.Content, ListCommandRegex);

            string key = regexMatch.Groups["key"].Value;
            if (String.IsNullOrWhiteSpace(key))
            {
                return;
            }

            var listRoot = GetRootByKey(key);
            var delDict = new Dictionary<string, XElement>();

            RPItemDictGenerator(listRoot, key + ".", delDict);

            //string msgContent = msg.Content;
            //string[] words = msgContent.Split(" ");

            //if (words.Length < 2)
            //{
            //    await msg.Channel.SendMessageAsync(@"Видалити що ? " + EmojiCodes.WaitWhat);
            //    return;
            //}

            List<string> citationsDeleted = new List<string>();

            //for (int i = 1; i < words.Length; i++)
            //{
            //    citationsToDelete.Add(words[i]);
            //}

            foreach (var delKVP in delDict)
            {
                DeleteItemRecursively(delKVP.Value);
                citationsDeleted.Add(delKVP.Key);
                try
                {
                    await Task.Run(() => File.Delete(
                        Path.Combine(_guildPath, _moduleFolder, delKVP.Value.Value)));
                }
                catch
                {
                    //
                }
            }

            //foreach (var citation in citationsToDelete)
            //{
            //    foreach (var citationEl in _moduleConfig.Root.Elements())
            //    {
            //        if (citation == citationEl.Name.ToString())
            //        {
            //            citationEl.Remove();
            //            citationsDeleted.Add(citation);
            //            break;
            //        }
            //    }
            //}

            if (citationsDeleted.Count > 0)
            {
                //await RaiseConfigChanged(_configEl);
                await ModuleConfigChanged().ConfigureAwait(false);
                Reconfigure(_configEl);
                //GenerateUseCommands(ExtractPermissions(_moduleConfig.Root.Attribute("usePerm")));
                string output = @"Видалив наступні цитати :" + Environment.NewLine + @"```";
                foreach (var deleted in citationsDeleted)
                {
                    output += deleted + Environment.NewLine;
                }
                output += "```" + Environment.NewLine + EmojiCodes.Pepe;

                await msg.Channel.SendMessageAsync(output).ConfigureAwait(false);
            }
            else
            {
                await msg.Channel.SendMessageAsync(@"Щооо ?? " + EmojiCodes.WaitWhat).ConfigureAwait(false);
            }
        }
        
        /// <summary>
        /// Creates default module config XML file and writes file to disk.
        /// </summary>
        /// <param name="filePath">Config filepath.</param>
        protected override void CreateDefaultModuleConfig(string filePath)
        {
            try
            {
                new XDocument(
                    new XElement(ModuleXmlName,
                        new XAttribute("cfgPerm", ""),
                        new XAttribute("addPerm", ""),
                        new XAttribute("usePerm", ""),
                        new XAttribute("delPerm", "")
                    )
                ).Save(filePath);
            }
            catch
            {
                // TODO : Handle Exception
            }
        }
    }
}
