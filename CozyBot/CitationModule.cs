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
    public class CitationModule : ContentModule
    {
        //Private Fields

        /// <summary>
        /// Filename of module config.
        /// </summary>
        private static string _configFileName = "CitationModuleConfig.xml";

        /// <summary>
        /// String module Identifier.
        /// </summary>
        private static string _stringID = "CitationModule";

        /// <summary>
        /// Module name in Guild config.
        /// </summary>
        private static string _moduleXmlName = "usercite";

        /// <summary>
        /// Module working path.
        /// </summary>
        private string _workingPath;

        /// <summary>
        /// String module identifier.
        /// </summary>
        public override string StringID { get { return _stringID; } }
        /// <summary>
        /// Module name in Guild config.
        /// </summary>
        public override string ModuleXmlName { get { return _moduleXmlName; } }

        /// <summary>
        /// Citation module constructor.
        /// </summary>
        /// <param name="configEl">XML Element containing Guild modules config.</param>
        /// <param name="adminIds">IDs of Guild admins.</param>
        /// <param name="clientId">Bot ID.</param>
        /// <param name="workingPath">Path to module working folder.</param>
        public CitationModule(XElement configEl, List<ulong> adminIds, ulong clientId, string workingPath)
            : base (configEl, adminIds, clientId)
        {
            _workingPath = workingPath ?? throw new ArgumentNullException("workingPath cannot be null!");

            if (!Directory.Exists(_workingPath))
            {
                Directory.CreateDirectory(_workingPath);
            }
            if (!Directory.Exists(_workingPath + @"usercite\"))
            {
                Directory.CreateDirectory(_workingPath + @"usercite\");
            }
        }

        protected override Func<SocketMessage, Task> UseCommandGenerator(string filepath)
        {
            return async (msg) =>
            {
                try
                {
                    await msg.DeleteAsync();
                }
                catch
                {

                }

                int linesCount = 0;
                using (StreamReader sr = new StreamReader(filepath))
                {
                    while (await sr.ReadLineAsync() != null)
                    {
                        linesCount++;
                    }
                }
                int selectedLineIndex = _rnd.Next() % linesCount;
                string selectedLine = String.Empty;

                int i = 0;
                using (StreamReader sr = new StreamReader(filepath))
                {
                    do
                    {
                        selectedLine = await sr.ReadLineAsync();
                    } while (i++ != selectedLineIndex);
                }

                SendTextMessageHumanLike(msg, selectedLine);
            };
        }

        private void SendTextMessageHumanLike(SocketMessage msg, string line)
        {
            Task.Run(
                async () =>
                {
                    await Task.Delay(1500 + (_rnd.Next() % 2000));
                    await msg.Channel.TriggerTypingAsync();
                    await Task.Delay(3000 + (_rnd.Next() % 4000));
                    await msg.Channel.SendMessageAsync(line);
                }
            );
        }

        protected override async Task AddCommand(SocketMessage msg)
        {
            try
            {
                await msg.DeleteAsync();
            }
            catch
            {

            }

            var words = msg.Content.Split(" ");

            if (words.Length < 3)
            {
                return;
            }

            string author = words[1];
            string citation = String.Empty;

            for (int i = 2; i < words.Length; i++)
            {
                citation += words[i];
                citation += " ";
            }

            string filepath = _workingPath + @"usercite\" + author + ".dat";
            bool existingAuthor = false;

            await Task.Run(
                () =>
                {
                    if (!Directory.Exists(_workingPath + @"usercite\"))
                    {
                        Directory.CreateDirectory(_workingPath + @"usercite\");
                    }
                }
            );

            foreach (var cite in _moduleConfigEl.Elements())
            {
                if (cite.Name == author)
                {
                    existingAuthor = true;
                    break;
                }
            }

            await SaveCitationToFile(filepath, citation);

            if (!existingAuthor)
            {
                _moduleConfigEl.Add(new XElement(author, filepath));

                await RaiseConfigChanged(_configEl);

                GenerateUseCommands(ExtractPermissions(_moduleConfigEl.Attribute("usePerm")));
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

            foreach (var citeEl in _moduleConfigEl.Elements())
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
                foreach (var citationEl in _moduleConfigEl.Elements())
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

                GenerateUseCommands(ExtractPermissions(_moduleConfigEl.Attribute("usePerm")));
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
    }
}
