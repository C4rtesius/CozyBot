using System;
using System.IO;
using System.Text;
using System.Xml.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Discord;
using Discord.WebSocket;

namespace DiscordBot1
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
        private static int _msgLengthLimit = 1980;

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
        private static string _moduleFolder = @"usercite\";

        /// <summary>
        /// String for citation usage count in XML.
        /// </summary>
        private static string _usageCountAttributeName = "used";

        /// <summary>
        /// Regex used in Add command parsing.
        /// </summary>
        private static string _addCommandRegex = @"^(?<pref>\S+)\s+(?<key>\S+)\s+(?<cite>[\s\S]+)$";

        /// <summary>
        /// Module XML config path.
        /// </summary>
        protected string _moduleConfigFilePath = String.Empty;

        // Public Properties

        /// <summary>
        /// String module identifier.
        /// </summary>
        public override string StringID { get { return _stringID; } }

        /// <summary>
        /// Module name in Guild XML config.
        /// </summary>
        public override string ModuleXmlName { get { return _moduleXmlName; } }

        /// <summary>
        /// Module XML config path.
        /// </summary>
        public override string ModuleConfigFilePath
        {
            get
            {
                if (_moduleConfigFilePath == String.Empty)
                {
                    _moduleConfigFilePath = _guildPath + _configFileName;
                }
                return _moduleConfigFilePath;
            }
        }

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
            if (!Directory.Exists(_guildPath + _moduleFolder))
            {
                Directory.CreateDirectory(_guildPath + _moduleFolder);
            }
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
                    await msg.DeleteAsync();
                }
                catch
                {
                    // TODO: add logging or specify concrete Exception (?)
                }

                XElement currentKeyEl = _moduleConfig.Root;
                XElement subEl = null;

                string[] keys = key.Split('.');

                for (int i = 0; i < keys.Length; i++)
                {
                    subEl = null;

                    foreach (var el in currentKeyEl.Elements("key"))
                    {
                        if (el.Attribute("name") != null)
                        {
                            if (String.Compare(el.Attribute("name").Value, keys[i]) == 0)
                            {
                                subEl = el;
                                break;
                            }
                        }
                    }
                    if (subEl == null)
                    {
                        foreach (var el in currentKeyEl.Elements("item"))
                        {
                            if (el.Attribute("name") != null)
                            {
                                if (String.Compare(el.Attribute("name").Value, keys[i]) == 0)
                                {
                                    subEl = el;
                                }
                            }
                        }
                    }
                    if (subEl == null)
                    {
                        return;
                    }
                    currentKeyEl = subEl;
                }

                var citationsList = GetItemsFromTree(currentKeyEl);

                //foreach (var el in _moduleConfig.Root.Elements("key"))
                //{
                //    if (String.Compare(el.Attribute("name").ToString(), key) == 0)
                //    {
                //        keyEl = el;
                //        break;
                //    }
                //}

                //if (keyEl == null)
                //{
                //    return;
                //}

                //var citationEls = new List<XElement>(keyEl.Elements("citation"));

                //if (citationEls.Count == 0)
                //{
                //    return;
                //}

                XElement citationEl = citationsList[_rnd.Next() % citationsList.Count];
                string citationFileName = citationEl.Value;

                string citation = String.Empty;
                try
                {
                    citation = File.ReadAllText(_guildPath + _moduleFolder + citationFileName);
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
                    await SendTextMessageHumanLike(msg, citation);
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
                        citationEl.Attribute(_usageCountAttributeName).Value = uses.ToString();
                    }
                    else
                    {
                        citationEl.Attribute(_usageCountAttributeName).Value = 1.ToString();
                    }
                }
                else
                {
                    citationEl.Add(
                        new XAttribute(
                            _usageCountAttributeName, 1.ToString()
                        )
                    );
                }

                await ModuleConfigChanged();
                Reconfigure(_configEl);
            };
        }

        /// <summary>
        /// Method for items extraction from tree.
        /// </summary>
        /// <param name="root">Root element from which to extract all items.</param>
        /// <returns>List of all items in tree.</returns>
        private List<XElement> GetItemsFromTree(XElement root)
        {
            List<XElement> result = new List<XElement>();
            foreach(var el in root.Elements("item"))
            {
                result.Add(el);
            }
            foreach(var key in root.Elements("key"))
            {
                result.AddRange(GetItemsFromTree(key));
            }
            return result;
        }

        /// <summary>
        /// Method to send messages in human-like fashion. Just for lulz.
        /// </summary>
        /// <param name="msg">SocketMessage which triggered action.</param>
        /// <param name="line">String to send.</param>
        /// <returns>Async Task performing sending message in human-like fashion.</returns>
        private async Task SendTextMessageHumanLike(SocketMessage msg, string line)
        {
            await Task.Delay(1500 + (_rnd.Next() % 2000));
            await msg.Channel.TriggerTypingAsync();
            await Task.Delay(3000 + (_rnd.Next() % 4000));
            await msg.Channel.SendMessageAsync(line);
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
                await msg.DeleteAsync();
            }
            catch
            {
                // TODO: add logging or specify concrete Exception (?)
            }

            if (!Regex.IsMatch(msg.Content, _addCommandRegex))
            {
                return;
            }

            var regexMatch = Regex.Match(msg.Content, _addCommandRegex);

            //var words = msg.Content.Split(" ");

            //if (words.Length < 3)
            //{
            //    return;
            //}

            //string key = words[1];
            //string citation = msg.Content.Remove(0, msg.Content.IndexOf(words[0]) + words[0].Length);
            //citation = citation.Remove(0, citation.IndexOf(words[1]) + words[1].Length);
            //citation = citation.Remove(0, citation.IndexOf(words[2]));

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
                File.WriteAllText(_guildPath + _moduleFolder + newItemFileName, regexMatch.Groups["cite"].Value);
            }
            catch (Exception ex)
            {
                return;
            }

            string[] keys = regexMatch.Groups["key"].Value.Split('.');

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
                await ModuleConfigChanged();
            }
            catch
            {
                // TODO : Implement
            }

            Reconfigure(_configEl);

            //string filepath = _guildPath + _moduleFolder + key + ".dat";
            //bool existingAuthor = false;

            //await Task.Run(
            //    () =>
            //    {
            //        if (!Directory.Exists(_guildPath + _moduleFolder))
            //        {
            //            Directory.CreateDirectory(_guildPath + _moduleFolder);
            //        }
            //    }
            //);

            //foreach (var cite in _moduleConfig.Root.Elements("citation"))
            //{
            //    if (String.Compare(cite.Attribute("name").ToString(), key) == 0)
            //    {
            //        existingAuthor = true;

            //        break;
            //    }
            //}

            //await SaveCitationToFile(filepath, citation);

            //if (!existingAuthor)
            //{
            //    _moduleConfig.Root.Add(
            //        new XElement(
            //            "citation", 
            //            new XAttribute("name", key),
            //            new XAttribute(_usageCountAttributeName, 0.ToString()),
            //            citation
            //        )
            //    );

                //await RaiseConfigChanged(_configEl);

            //    GenerateUseCommands(ExtractPermissions(_moduleConfig.Root.Attribute("usePerm")));
            //}

            await msg.Channel.SendMessageAsync("Записав цитатку " + EmojiCodes.DankPepe);
        }

        private async Task SaveCitationToFile(string filepath, string cite)
        {
            using (StreamWriter file = new StreamWriter(filepath, true))
            {
                await file.WriteLineAsync(cite);
            }
        }

        protected override async Task ListCommand(SocketMessage msg)
        {
            try
            {
                await msg.DeleteAsync();
            }
            catch { }

            string output = @"**Список доступних цитат :**" + Environment.NewLine + @"```";

            foreach (var citeEl in _moduleConfig.Root.Elements())
            {
                output += Environment.NewLine + citeEl.Name.ToString();
            }

            output += @"```";

            var ch = await msg.Author.GetOrCreateDMChannelAsync();

            await ch.SendMessageAsync(output);

            output = msg.Author.Mention + " подивись в приватні повідомлення " + EmojiCodes.Bumagi;

            await msg.Channel.SendMessageAsync(output);
        }

        protected override async Task HelpCommand(SocketMessage msg)
        {
            if (msg.Author is SocketGuildUser user)
            {
                try
                {
                    await msg.DeleteAsync();
                }
                catch
                {

                }

                var guild = user.Guild;
                string iconUrl = guild.IconUrl;

                var eba = new EmbedAuthorBuilder
                {
                    Name = @"Shining Armor",
                    IconUrl = @"https://cdn.discordapp.com/avatars/335004246007218188/3094a7be163d3cd1d03278b53c8f08eb.png"
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

                await dm.SendMessageAsync(String.Empty, false, eb.Build());

                string output = msg.Author.Mention + " подивись в приватні повідомлення " + EmojiCodes.Bumagi;

                await msg.Channel.SendMessageAsync(output);
            }
        }

        protected override async Task DeleteCommand(SocketMessage msg)
        {
            string msgContent = msg.Content;
            string[] words = msgContent.Split(" ");

            if (words.Length < 2)
            {
                await msg.Channel.SendMessageAsync(@"Видалити що ? " + EmojiCodes.WaitWhat);
                return;
            }

            List<string> citationsToDelete = new List<string>();
            List<string> citationsDeleted = new List<string>();

            for (int i = 1; i < words.Length; i++)
            {
                citationsToDelete.Add(words[i]);
            }

            foreach (var citation in citationsToDelete)
            {
                foreach (var citationEl in _moduleConfig.Root.Elements())
                {
                    if (citation == citationEl.Name.ToString())
                    {
                        citationEl.Remove();
                        citationsDeleted.Add(citation);
                        break;
                    }
                }
            }

            if (citationsDeleted.Count > 0)
            {
                await RaiseConfigChanged(_configEl);

                GenerateUseCommands(ExtractPermissions(_moduleConfig.Root.Attribute("usePerm")));
                string output = @"Видалив наступні цитати :" + Environment.NewLine + @"``` ";
                foreach (var deleted in citationsDeleted)
                {
                    output += deleted + " ";
                }
                output += "```" + Environment.NewLine + EmojiCodes.Pepe;

                await msg.Channel.SendMessageAsync(output);
            }
            else
            {
                await msg.Channel.SendMessageAsync(@"Щооо ?? " + EmojiCodes.WaitWhat);
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
