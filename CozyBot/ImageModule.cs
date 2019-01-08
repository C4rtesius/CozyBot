using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;
using System.Threading.Tasks;
using System.IO;
using System.Net.Http;

using Discord;
using Discord.WebSocket;

namespace DiscordBot1
{
    /// <summary>
    /// ContentModule specialization - works with images(/video?).
    /// </summary>
    public class ImageModule : ContentModule
    {
        // Private Fields

        /// <summary>
        /// Used for Discord message limit check.
        /// </summary>
        private const int _messageLimit = 1995;

        /// <summary>
        /// Filename of module config.
        /// </summary>
        private static string _configFileName = "ImageModuleConfig.xml";

        /// <summary>
        /// String module Identifier.
        /// </summary>
        private static string _stringID = "ImageModule";

        /// <summary>
        /// Module name in Guild XML config.
        /// </summary>
        private static string _moduleXmlName = "userimg";

        /// <summary>
        /// Module working folder.
        /// </summary>
        private static string _moduleFolder = @"userimg\";

        /// <summary>
        /// Module XML config path.
        /// </summary>
        protected string _moduleConfigFilePath = String.Empty;

        protected string _filesPath = String.Empty;

        // Public Properties

        /// <summary>
        /// String module Identifier.
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
        /// ImageModule constructor.
        /// </summary>
        /// <param name="configEl">XML Element containing Guild modules config.</param>
        /// <param name="adminIds">IDs of Guild admins.</param>
        /// <param name="clientId">Bot ID.</param>
        /// <param name="workingPath">Path to module working folder.</param>
        public ImageModule(XElement configEl, List<ulong> adminIds, ulong clientId, string workingPath)
            : base(configEl, adminIds, clientId, workingPath)
        {
            _filesPath = _guildPath + _moduleFolder;
            if (!Directory.Exists(_filesPath))
            {
                Directory.CreateDirectory(_filesPath);
            }
        }

        public static Func<SocketMessage, Task> SendFileCommandGenerator(string filePath)
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
                await msg.Channel.SendFileAsync(filePath);
            };
        }

        protected override void GenerateAddCommands(List<ulong> perms)
        {
            base.GenerateAddCommands(perms);

            List<ulong> allPerms = new List<ulong>(_adminIds);
            allPerms.AddRange(perms);
            Rule addRule = RuleGenerator.HasRoleByIds(allPerms) & 
                (
                    RuleGenerator.TextIdentity(_prefix) &
                    RuleGenerator.HasImage
                );

            IBotCommand addCmd =
                new BotCommand(
                    StringID + "-add2cmd",
                    addRule,
                    DownloadImagesCommand
                );

            _addCommands.Add(addCmd);
        }

        protected async Task DownloadImagesCommand(SocketMessage msg)
        {
            foreach (var att in msg.Attachments)
            {
                if (RuleGenerator.IsImage(att))
                {
                    await DownloadFile(att, msg.Channel);
                }
            }

            try
            {
                await msg.DeleteAsync();
            }
            catch
            {

            }
        }

        protected async Task DownloadFile(Attachment att, ISocketMessageChannel sc)
        {
            await DownloadFile(att, sc, _filesPath + att.Filename);
        }

        protected async Task DownloadFile(Attachment att, ISocketMessageChannel sc, string filepath)
        {
            if (File.Exists(filepath))
            {
                await sc.SendMessageAsync("Пікча з такою назвою вже є " + EmojiCodes.Tomas);
                return;
            }

            using (HttpClient hc = new HttpClient())
            {
                using (FileStream fs = new FileStream(filepath, FileMode.Create, FileAccess.Write))
                {
                    var response = await hc.GetAsync(att.Url);
                    await response.Content.CopyToAsync(fs);
                }
            }

            await sc.SendMessageAsync("Зберіг пікчу " + EmojiCodes.DankPepe);
        }

        protected override async Task AddCommand(SocketMessage msg)
        {
            var words = msg.Content.Split(" ");
            bool ok = true;
            if (words.Length > 1)
            {
                string imgName = words[1];

                if (!Directory.Exists(_filesPath))
                {
                    Directory.CreateDirectory(_filesPath);
                }
                foreach (var img in _moduleConfig.Root.Elements())
                {
                    if (img.Name == words[1])
                    {
                        await msg.Channel.SendMessageAsync("Пікча з таким кодом вже є " + EmojiCodes.Tomas);
                        ok = false;
                        break;
                    }
                }
                if (ok)
                {
                    foreach (var att in msg.Attachments)
                    {
                        if (RuleGenerator.IsImage(att))
                        {
                            string filepath = _filesPath + att.Filename;

                            if (File.Exists(filepath))
                            {
                                filepath = _filesPath + Guid.NewGuid().ToString() + Path.GetExtension(att.Filename);
                            }

                            await DownloadFile(att, msg.Channel, filepath);
                            _moduleConfig.Root.Add(new XElement(imgName, filepath));

                            await RaiseConfigChanged(_configEl);

                            GenerateUseCommands(ExtractPermissions(_moduleConfig.Root.Attribute("usePerm")));

                            break;
                        }
                    }
                }
            }
            else
            {
                await msg.Channel.SendMessageAsync(@"Щооо ?? " + EmojiCodes.Tomas);
            }

            try
            {
                await msg.DeleteAsync();
            }
            catch
            {

            }
        }

        protected override void GenerateUseCommands(List<ulong> perms)
        {
            base.GenerateUseCommands(perms);

            List<ulong> allPerms = new List<ulong>(_adminIds);
            allPerms.AddRange(perms);
            Rule useRuleRandom = RuleGenerator.HasRoleByIds(allPerms) & 
                RuleGenerator.TextIdentity(_prefix) &
                !RuleGenerator.HasImage;

            _useCommands.Add(
                new BotCommand(
                    StringID + "-random-usecmd",
                    useRuleRandom,
                    RandomImageCommand
                )   
            );
        }

        protected override Func<SocketMessage, Task> UseCommandGenerator(string key)
        {
            return SendFileCommandGenerator(key);
        }

        protected async Task RandomImageCommand(SocketMessage msg)
        {
            try
            {
                await msg.DeleteAsync();
            }
            catch
            {

            }

            string[] imagesArray = new string[] { };
            var imagesList = new List<string>();

            await Task.Run(
                () =>
                {
                    var files = Directory.GetFiles(_filesPath);
                    foreach (var file in files)
                    {
                        if (file.EndsWith(".jpeg") ||
                            file.EndsWith(".jpg") ||
                            file.EndsWith(".png") ||
                            file.EndsWith(".bmp") ||
                            file.EndsWith(".gif")
                        )
                        {
                            imagesList.Add(file);
                        }
                    }

                    imagesArray = imagesList.ToArray();
                }
            );

            int count = imagesArray.Length;
            if (count == 0)
            {
                return;
            }

            string selectedFile = imagesArray[_rnd.Next() % count];

            await SendImageByFilepath(msg, selectedFile);
        }

        protected async Task SendImageByFilepath(SocketMessage msg, string filePath)
        {
            await msg.Channel.SendFileAsync(filePath);
        }

        protected override async Task ListCommand(SocketMessage msg)
        {
            try
            {
                await msg.DeleteAsync();
            }
            catch { }

            List<string> outputMsgs = new List<string>();

            string output = @"**Список доступних зображень :**" + Environment.NewLine + @"```";

            string name = String.Empty;

            foreach (var citeEl in _moduleConfig.Root.Elements())
            {
                name = citeEl.Name.ToString();

                if (output.Length + name.Length < _messageLimit)
                {
                    output += Environment.NewLine + citeEl.Name.ToString();
                }
                else
                {
                    output += @"```";
                    outputMsgs.Add(output);
                    output = @"```" + name;
                }
            }

            output += @"```";
            outputMsgs.Add(output);

            var ch = await msg.Author.GetOrCreateDMChannelAsync();

            foreach (var outputMsg in outputMsgs)
            {
                await ch.SendMessageAsync(outputMsg);
            }

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
                    Name = "Команди пікчевого модуля",
                    Value =
                    _prefix + @"cfg perm [use/add/del/cfg] @Роль1 @Роль2 ... - виставлення прав доступу до команд" + Environment.NewLine +
                    _prefix + @"add ключ [пікча] - додати пікчу з ключем" + Environment.NewLine +
                    _prefix + @"del ключ - видалити ключ та пов'язану з ним пікчу" + Environment.NewLine +
                    _prefix + @"list - отримати список доступних ключів у Приватних Повідомленнях" + Environment.NewLine +
                    _prefix + @"ключ - отримати пікчу за ключем" + Environment.NewLine +
                    _prefix + @" [пікча] - зберегти пікчу" + Environment.NewLine +
                    _prefix + @" - отримати випадкову пікчу" + Environment.NewLine +
                    _prefix + @"help - цей список команд"
                };

                var efob = new EmbedFooterBuilder
                {
                    Text = "Оффнуть картинки - еще не самое проблемное."
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

            List<string> imagesToDelete = new List<string>();
            Dictionary<string, string> imagesDeleted = new Dictionary<string, string>();

            for (int i = 1; i < words.Length; i++)
            {
                imagesToDelete.Add(words[i]);
            }

            foreach (var citation in imagesToDelete)
            {
                foreach (var citationEl in _moduleConfig.Root.Elements())
                {
                    if (citation == citationEl.Name.ToString())
                    {
                        citationEl.Remove();
                        imagesDeleted.Add(citation, citationEl.Value);
                        await Task.Run( () => File.Delete(citationEl.Value));
                        break;
                    }
                }
            }

            if (imagesDeleted.Count > 0)
            {
                await RaiseConfigChanged(_configEl);

                GenerateUseCommands(ExtractPermissions(_moduleConfig.Root.Attribute("usePerm")));
                string output = @"Видалив наступні зображення :" + Environment.NewLine + @"```";
                foreach (var deleted in imagesDeleted)
                {
                    output += deleted.Key + " ";
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