using System;
using System.IO;
using System.Xml.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Discord;
using Discord.WebSocket;

namespace CozyBot
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
        private const int _msgLengthLimit = 1800;

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
        private static string _moduleFolder = @"userimg";

        /// <summary>
        /// String for citation usage count in XML.
        /// </summary>
        private static string _usageCountAttributeName = "used";

        /// <summary>
        /// Regex used in Add command parsing.
        /// </summary>
        private string _addCommandRegex = @"^(?<pref>\S+)\s+(?<key>\S+)$";

        /// <summary>
        /// Module XML config path.
        /// </summary>
        protected string _moduleConfigFilePath = String.Empty;

        /// <summary>
        /// Forbidden keys (because they are valid commands).
        /// </summary>
        protected string[] _blacklistedKeys = new string[]
        {
            "add",
            "list",
            // verbose list not implemented yet
            //"vlist",   
            "help",
            "cfg",
            "del"
        };

        /// <summary>
        /// Regex used in Add command parsing.
        /// </summary>
        protected override string AddCommandRegex
        {
            get
            {
                return _addCommandRegex;
            }
        }

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
                    _moduleConfigFilePath = Path.Combine(_guildPath, _configFileName);
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
            if (!Directory.Exists(Path.Combine(_guildPath, _moduleFolder)))
            {
                Directory.CreateDirectory(Path.Combine(_guildPath, _moduleFolder));
            }
        }

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

                var imgList = GetItemsListByKey(key);
                if (imgList.Count == 0)
                {
                    return;
                }

                XElement imgEl = imgList[_rnd.Next() % imgList.Count];
                string imgFileName = imgEl.Value;

                try
                {
                    await SendFileTask(msg, Path.Combine(_guildPath, _moduleFolder, imgFileName));
                }
                catch
                {
                    // TODO : implement more concise exception handling (?)
                    return;
                }

                if (imgEl.Attribute(_usageCountAttributeName) != null)
                {
                    if (Int32.TryParse(imgEl.Attribute(_usageCountAttributeName).Value, out int uses))
                    {
                        uses++;
                        imgEl.Attribute(_usageCountAttributeName).Value = uses.ToString();
                    }
                    else
                    {
                        imgEl.Attribute(_usageCountAttributeName).Value = 1.ToString();
                    }
                }
                else
                {
                    imgEl.Add(
                        new XAttribute(
                            _usageCountAttributeName, 1.ToString()
                        )
                    );
                }

                await ModuleConfigChanged();
                Reconfigure(_configEl);
            };
            // old code
            //return SendFileCommandGenerator(key);
        }

        /// <summary>
        /// Send file command generator.
        /// </summary>
        /// <param name="msg">SocketMessage which triggered action.</param>
        /// <param name="filePath">Path to file to send.</param>
        /// <returns>Async Task sending specified file to SocketMessage channel.</returns>
        public async Task SendFileTask(SocketMessage msg, string filePath)
        {
            try
            {
                await msg.Channel.SendFileAsync(filePath);
            }
            catch
            {
                // TODO : Implement logging / specific catching.
            }
        }

        /// <summary>
        /// Generates addition commands and adds them to module commands list.
        /// </summary>
        /// <param name="perms">List of Roles IDs which are allowed to use Add commands.</param>
        protected override void GenerateAddCommands(List<ulong> perms)
        {
            base.GenerateAddCommands(perms);

            // adding images without keys is deprecated

            //List<ulong> allPerms = new List<ulong>(_adminIds);
            //allPerms.AddRange(perms);
            //Rule addRule = RuleGenerator.HasRoleByIds(allPerms) & 
            //    (
            //        RuleGenerator.TextIdentity(_prefix) &
            //        RuleGenerator.HasImage
            //    );

            //IBotCommand addCmd =
            //    new BotCommand(
            //        StringID + "-add2cmd",
            //        addRule,
            //        DownloadImagesCommand
            //    );

            //_addCommands.Add(addCmd);
        }

        /// <summary>
        /// Downloads Images from input SocketMessage.
        /// </summary>
        /// <param name="msg">SocketMessage containing image.</param>
        /// <returns>Async Task performing job.</returns>
        //protected async Task DownloadImagesCommand(SocketMessage msg)
        //{
        //    foreach (var att in msg.Attachments)
        //    {
        //        if (RuleGenerator.IsImage(att))
        //        {
        //            try
        //            {
        //                await DownloadFile(att, msg.Channel);
        //            }
        //            catch
        //            {
        //                // TODO : Implement logging/exception handling
        //            }
                    
        //            // More than one image is unsupported

        //            break;
        //        }
        //    }

        //    try
        //    {
        //        await msg.DeleteAsync();
        //    }
        //    catch
        //    {

        //    }
        //}

        /// <summary>
        /// Downloads Image from Attachment.
        /// </summary>
        /// <param name="att">Attachment containing image.</param>
        /// <param name="sc">ISocketMessageChannel ...</param>
        /// <returns>Async Task downloading image.</returns>
        //protected async Task DownloadFile(Attachment att, ISocketMessageChannel sc)
        //{
        //    await DownloadFile(att, sc, _guildPath + _moduleFolder + att.Filename);
        //}

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

            XElement newItem = new XElement("item");

            try
            {
                foreach (var att in msg.Attachments)
                {
                    if (RuleGenerator.IsImage(att))
                    {
                        Guid newItemGuid = Guid.NewGuid();

                        string newItemFileName = newItemGuid.ToString() + Path.GetExtension(att.Filename);

                        newItem =
                            new XElement(
                                "item",
                                new XAttribute("name", newItemGuid.ToString()),
                                newItemFileName
                            );

                        string filepath = Path.Combine(_guildPath, _moduleFolder, newItemFileName);

                        await DownloadFile(att, msg.Channel, filepath);
                        break;
                    }
                }
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
                await ModuleConfigChanged();
            }
            catch
            {
                // TODO : Implement
            }

            Reconfigure(_configEl);

            try
            {
                await msg.DeleteAsync();
            }
            catch
            {
                // TODO: add logging or specify concrete Exception (?)
            }
        }

        protected override void GenerateUseCommands(List<ulong> perms)
        {
            base.GenerateUseCommands(perms);

            // TODO: Implement verbose list cmd (?)

            //List<ulong> allListPerms = new List<ulong>(_adminIds);
            //allListPerms.AddRange(perms);
            //Rule listRule = RuleGenerator.HasRoleByIds(allListPerms)
            //    & RuleGenerator.PrefixatedCommand(_prefix, "vlist");

            //_useCommands.Add(
            //    new BotCommand(
            //        StringID + "-listcmd",
            //        listRule,
            //        VerboseListCommand
            //    )
            //);
        }

        // deprecated, implemented in UseCommand

        //protected async Task RandomImageCommand(SocketMessage msg)
        //{
        //    try
        //    {
        //        await msg.DeleteAsync();
        //    }
        //    catch
        //    {

        //    }

        //    string[] imagesArray = new string[] { };
        //    var imagesList = new List<string>();

        //    await Task.Run(
        //        () =>
        //        {
        //            var files = Directory.GetFiles(_moduleConfigFilePath);
        //            foreach (var file in files)
        //            {
        //                if (file.EndsWith(".jpeg") ||
        //                    file.EndsWith(".jpg") ||
        //                    file.EndsWith(".png") ||
        //                    file.EndsWith(".bmp") ||
        //                    file.EndsWith(".gif")
        //                )
        //                {
        //                    imagesList.Add(file);
        //                }
        //            }

        //            imagesArray = imagesList.ToArray();
        //        }
        //    );

        //    int count = imagesArray.Length;
        //    if (count == 0)
        //    {
        //        return;
        //    }

        //    string selectedFile = imagesArray[_rnd.Next() % count];

        //    await SendImageByFilepath(msg, selectedFile);
        //}

        //protected async Task SendImageByFilepath(SocketMessage msg, string filePath)
        //{
        //    await msg.Channel.SendFileAsync(filePath);
        //}

        protected override async Task ListCommand(SocketMessage msg)
        {
            // TODO : fix a bug with wrong `list` command output
            try
            {
                await msg.DeleteAsync();
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

            string output = @"**Список доступних пікч"
                + ((String.IsNullOrWhiteSpace(keyStr) ? "" : @" по підключу `" + keyStr + @"`"))
                + @" :**" + Environment.NewLine + @"```";

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
                await ch.SendMessageAsync(outputMsg);
            }

            output = msg.Author.Mention + " подивись в приватні повідомлення " + EmojiCodes.Bumagi;

            try
            {
                await msg.Channel.SendMessageAsync(output);
            }
            catch
            {
                //
            }
            // old code
            //List<string> outputMsgs = new List<string>();

            //string output = @"**Список доступних зображень :**" + Environment.NewLine + @"```";

            //string name = String.Empty;

            //foreach (var citeEl in _moduleConfig.Root.Elements())
            //{
            //    name = citeEl.Name.ToString();

            //    if (output.Length + name.Length < _msgLengthLimit)
            //    {
            //        output += Environment.NewLine + citeEl.Name.ToString();
            //    }
            //    else
            //    {
            //        output += @"```";
            //        outputMsgs.Add(output);
            //        output = @"```" + name;
            //    }
            //}

            //output += @"```";
            //outputMsgs.Add(output);

            //var ch = await msg.Author.GetOrCreateDMChannelAsync();

            //foreach (var outputMsg in outputMsgs)
            //{
            //    await ch.SendMessageAsync(outputMsg);
            //}

            //output = msg.Author.Mention + " подивись в приватні повідомлення " + EmojiCodes.Bumagi;

            //await msg.Channel.SendMessageAsync(output);
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

            List<string> imgDeleted = new List<string>();

            foreach (var delKVP in delDict)
            {
                DeleteItemRecursively(delKVP.Value);
                imgDeleted.Add(delKVP.Key);
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

            if (imgDeleted.Count > 0)
            {
                //await RaiseConfigChanged(_configEl);
                await ModuleConfigChanged();
                Reconfigure(_configEl);
                //GenerateUseCommands(ExtractPermissions(_moduleConfig.Root.Attribute("usePerm")));
                string output = @"Видалив наступні пікчі :" + Environment.NewLine + @"```";
                foreach (var deleted in imgDeleted)
                {
                    output += deleted + Environment.NewLine;
                }
                output += "```" + Environment.NewLine + EmojiCodes.Pepe;

                await msg.Channel.SendMessageAsync(output);
            }
            else
            {
                await msg.Channel.SendMessageAsync(@"Щооо ?? " + EmojiCodes.WaitWhat);
            }

            // old code
            //string msgContent = msg.Content;
            //string[] words = msgContent.Split(" ");

            //if (words.Length < 2)
            //{
            //    await msg.Channel.SendMessageAsync(@"Видалити що ? " + EmojiCodes.WaitWhat);
            //    return;
            //}

            //List<string> imagesToDelete = new List<string>();
            //Dictionary<string, string> imagesDeleted = new Dictionary<string, string>();

            //for (int i = 1; i < words.Length; i++)
            //{
            //    imagesToDelete.Add(words[i]);
            //}

            //foreach (var citation in imagesToDelete)
            //{
            //    foreach (var citationEl in _moduleConfig.Root.Elements())
            //    {
            //        if (citation == citationEl.Name.ToString())
            //        {
            //            citationEl.Remove();
            //            imagesDeleted.Add(citation, citationEl.Value);
            //            await Task.Run( () => File.Delete(citationEl.Value));
            //            break;
            //        }
            //    }
            //}

            //if (imagesDeleted.Count > 0)
            //{
            //    await RaiseConfigChanged(_configEl);

            //    GenerateUseCommands(ExtractPermissions(_moduleConfig.Root.Attribute("usePerm")));
            //    string output = @"Видалив наступні зображення :" + Environment.NewLine + @"```";
            //    foreach (var deleted in imagesDeleted)
            //    {
            //        output += deleted.Key + " ";
            //    }
            //    output += "```" + Environment.NewLine + EmojiCodes.Pepe;

            //    await msg.Channel.SendMessageAsync(output);
            //}
            //else
            //{
            //    await msg.Channel.SendMessageAsync(@"Щооо ?? " + EmojiCodes.WaitWhat);
            //}
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