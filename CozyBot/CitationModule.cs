using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;
using System.Threading.Tasks;
using System.IO;

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

                XElement keyEl = null;

                foreach (var el in _moduleConfig.Root.Elements("key"))
                {
                    if (String.Compare(el.Attribute("name").ToString(), key) == 0)
                    {
                        keyEl = el;
                        break;
                    }
                }

                if (keyEl == null)
                {
                    return;
                }

                var citationEls = new List<XElement>(keyEl.Elements("citation"));

                if (citationEls.Count == 0)
                {
                    return;
                }

                int selectedIndex = _rnd.Next() % citationEls.Count;

                string filepath = citationEls[selectedIndex].Value.ToString();

                if (!File.Exists(filepath))
                {
                    return;
                }

                string citation = String.Empty;

                using (StreamReader sr = new StreamReader(filepath))
                {
                    citation = await sr.ReadToEndAsync();
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

                if (keyEl.Attribute(_usageCountAttributeName) != null)
                {
                    if (Int32.TryParse(keyEl.Attribute(_usageCountAttributeName).Value, out int uses))
                    {
                        uses++;
                        keyEl.Attribute(_usageCountAttributeName).Value = uses.ToString();
                        await RaiseConfigChanged(_configEl);
                    }
                }
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

            var words = msg.Content.Split(" ");

            if (words.Length < 3)
            {
                return;
            }

            string key = words[1];
            string citation = String.Empty;


            for (int i = 2; i < words.Length; i++)
            {
                citation += words[i];
                citation += " ";
            }

            string filepath = _guildPath + _moduleFolder + key + ".dat";
            bool existingAuthor = false;

            await Task.Run(
                () =>
                {
                    if (!Directory.Exists(_guildPath + _moduleFolder))
                    {
                        Directory.CreateDirectory(_guildPath + _moduleFolder);
                    }
                }
            );

            foreach (var cite in _moduleConfig.Root.Elements())
            {
                if (cite.Name == key)
                {
                    existingAuthor = true;
                    break;
                }
            }

            await SaveCitationToFile(filepath, citation);

            if (!existingAuthor)
            {
                _moduleConfig.Root.Add(new XElement(key, filepath));

                await RaiseConfigChanged(_configEl);

                GenerateUseCommands(ExtractPermissions(_moduleConfig.Root.Attribute("usePerm")));
            }

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
