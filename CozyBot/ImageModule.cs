using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Net.Http;
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
  /// ContentModule specialization - works with images(/video?).
  /// </summary>
  public class ImageModule : ContentModule
  {
    // Private Fields

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
    private static string _moduleFolder = "userimg";

    /// <summary>
    /// String for citation usage count in XML.
    /// </summary>
    private static string _usageCountAttributeName = "used";

    /// <summary>
    /// ConcurrentDictionary to implement ratelimiting per user, per channel, per command key.
    /// </summary>
    private ConcurrentDictionary<string, Task> _ratelimitDict;

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
      "vlist", // not implemented yet
      "help",
      "cfg",
      "del",
      "search"
    };

    /// <summary>
    /// Regex used in Add command parsing.
    /// </summary>
    protected override string AddCommandRegex => @"^(?<pref>\S+)\s+(?<key>\S+)$";

    // Public Properties

    /// <summary>
    /// String module Identifier.
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
      => String.IsNullOrEmpty(_moduleConfigFilePath) ?
         _moduleConfigFilePath = Path.Combine(_guildPath, _configFileName) :
         _moduleConfigFilePath;

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
        Directory.CreateDirectory(Path.Combine(_guildPath, _moduleFolder));

      _ratelimitDict = new ConcurrentDictionary<string, Task>();
    }

    protected override Func<SocketMessage, Task> UseCommandGenerator(string key)
    {
      return async (msg) =>
      {
        await msg.DeleteAsyncSafe($"[{LogPref}][USE]").ConfigureAwait(false);

        string dictKey = $"{msg.Author.Id}{msg.Channel.Id}{key}";
        if (_ratelimitDict.ContainsKey(dictKey))
          return;
        _ratelimitDict.TryAdd(dictKey, Task.Run(() => { Thread.Sleep(10000); _ratelimitDict.TryRemove(dictKey, out _); }));

        var imgList = GetItemsListByKey(key);
        if (imgList.Count == 0)
          return;

        XElement imgEl = imgList[_rnd.Next() % imgList.Count];
        string imgFileName = imgEl.Value;

        try
        {
          await SendFileTask(msg, Path.Combine(_guildPath, _moduleFolder, imgFileName)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          ex.LogToConsole($"[{LogPref}] File send failed: {key}");
          throw;
        }

        var usageAttr = imgEl.Attribute(_usageCountAttributeName);

        if (usageAttr != null)
          usageAttr.Value = Int32.TryParse(usageAttr.Value, out int uses) ? $"{++uses}" : "1";
        else
          imgEl.Add(new XAttribute(_usageCountAttributeName, "1"));

        await ModuleConfigChanged().ConfigureAwait(false);
        Reconfigure(_configEl);
      };
    }

    /// <summary>
    /// Send file command generator.
    /// </summary>
    /// <param name="msg">SocketMessage which triggered action.</param>
    /// <param name="filePath">Path to file to send.</param>
    /// <returns>Async Task sending specified file to SocketMessage channel.</returns>
    public async Task SendFileTask(SocketMessage msg, string filePath)
      => await msg.Channel.SendFileAsync(filePath).ConfigureAwait(false);

    protected async Task DownloadFile(Attachment att, ISocketMessageChannel sc, string filepath)
    {
      if (File.Exists(filepath))
      {
        await sc.SendMessageAsyncSafe($"Пікча з такою назвою вже є {EmojiCodes.Tomas}").ConfigureAwait(false);
        return;
      }

      using (HttpClient hc = new HttpClient())
        using (FileStream fs = new FileStream(filepath, FileMode.Create, FileAccess.Write))
          await (await hc.GetAsync(new Uri(att.Url)).ConfigureAwait(false)).Content.CopyToAsync(fs).ConfigureAwait(false);
    }

    protected override async Task AddCommand(SocketMessage msg)
    {
      string cmdPrefix = $"[{LogPref}][ADD]";
      await msg.DeleteAsyncSafe(cmdPrefix).ConfigureAwait(false);
      if (!Regex.IsMatch(msg.Content, AddCommandRegex))
        return;

      var regexMatch = Regex.Match(msg.Content, AddCommandRegex);

      string[] keys = regexMatch.Groups["key"].Value.Split('.');

      if (_blacklistedKeys.Any(blKey => blKey.ExactAs(keys[0])))
        return;

      XElement newItem;

      try
      {
        Attachment att = msg.Attachments.FirstOrDefault(att => RuleGenerator.IsImage(att));
        if (att == null)
          return;

        string newItemGuidString = $"{Guid.NewGuid()}";
        string newItemFileName = $"{newItemGuidString}{Path.GetExtension(att.Filename)}";

        newItem = new XElement("item",
                               new XAttribute("name", newItemGuidString),
                               newItemFileName);

        string filepath = Path.Combine(_guildPath, _moduleFolder, newItemFileName);

        await DownloadFile(att, msg.Channel, filepath).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        ex.LogToConsole($"{cmdPrefix} File download failed: {regexMatch.Groups["key"].Value}");
        throw;
      }

      XElement currentEl = _moduleConfig.Root;

      foreach (var key in keys)
      {
        XElement newEl = currentEl.Elements("key").FirstOrDefault(
          el => el.Attribute("name") != null && key.ExactAs(el.Attribute("name").Value));

        if (newEl == null)
        {
          newEl = new XElement("key", new XAttribute("name", key));
          currentEl.Add(newEl);
        }
        currentEl = newEl;
      }
      currentEl.Add(newItem);

      try
      {
        await ModuleConfigChanged().ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        ex.LogToConsole($"{cmdPrefix} Config save failed.");
        throw;
      }

      Reconfigure(_configEl);
      await msg.Channel.SendMessageAsyncSafe("Зберіг пікчу " + EmojiCodes.DankPepe).ConfigureAwait(false);
    }

    protected override async Task ListCommand(SocketMessage msg)
    {
      string cmdPrefix = $"[{LogPref}][LIST]";
      await msg.DeleteAsyncSafe(cmdPrefix).ConfigureAwait(false);

      var regexMatch = Regex.Match(msg.Content, ListCommandRegex);

      if (!regexMatch.Success)
        return;

      string keyStr = regexMatch.Groups["key"].Value;

      var list = RPKeyListGenerator(GetRootByKey(keyStr),
                                    String.IsNullOrWhiteSpace(keyStr) ? String.Empty : $"{keyStr}.",
                                    false);
      if (list.Count == 0)
        return;
      list.Add(keyStr);

      string output = String.Concat("**Список доступних пікч",
                                    String.IsNullOrWhiteSpace(keyStr) ? String.Empty : $" за ключем `{keyStr}`",
                                    $":**{Environment.NewLine}```{Environment.NewLine}");

      var dm = await msg.Author.GetOrCreateDMChannelAsync().ConfigureAwait(false);
      await dm.GenerateAndSendOutputMessages(output,
                                             list,
                                             s => $"{s}{Environment.NewLine}",
                                             s => $"```{Environment.NewLine}{s}",
                                             s => $"{s}```").ConfigureAwait(false);

      output = $"{msg.Author.Mention} подивись в приватні повідомлення {EmojiCodes.Bumagi}";
      await msg.Channel.SendMessageAsyncSafe(output).ConfigureAwait(false);
    }

    protected override async Task HelpCommand(SocketMessage msg)
    {
      if (!(msg.Author is SocketGuildUser user))
        return;

      await msg.DeleteAsyncSafe($"[{LogPref}][HELP]").ConfigureAwait(false);

      //var dm = await msg.Author.GetOrCreateDMChannelAsync().ConfigureAwait(false);
      await msg.Channel.SendMessageAsync(String.Empty, false, BuildHelpEmbed(user)).ConfigureAwait(false);

      //string output = $"{msg.Author.Mention} подивись в приватні повідомлення {EmojiCodes.Bumagi}";
      //await msg.Channel.SendMessageAsyncSafe(output).ConfigureAwait(false);
    }

    private Embed BuildHelpEmbed(SocketGuildUser user)
    {
      var guild = user.Guild;
      string iconUrl = guild.IconUrl;

      var eba = new EmbedAuthorBuilder
      {
        Name = guild.GetUser(_clientId).Nickname,
        IconUrl = guild.GetUser(_clientId).GetAvatarUrl()
      };

      var efb = new EmbedFieldBuilder
      {
        IsInline = false,
        Name = "Команди пікчевого модуля",
        Value = String.Join(Environment.NewLine,
                            @$"`{_prefix}cfg perm [use/add/del/cfg] @Роль1 @Роль2 ...` - налаштування доступу до команд",
                            @$"`{_prefix}add ключ [пікча]` - зберегти пікчу з ключем",
                            $@"`{_prefix}search запит` - знайти пікчу по ключу за запитом",
                            @$"`{_prefix}list [ключ]` - отримати список підключів за ключем",
                            @$"`{_prefix}del ключ` - видалити ключ та пов'язані з ним пікчі",
                            @$"`{_prefix}ключ` - отримати пікчу за ключем",
                            @$"`{_prefix}help` - цей список команд",
                            @$"`{_prefix}` - отримати випадкову пікчу")
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
      return eb.Build();
    }

    protected override async Task DeleteCommand(SocketMessage msg)
    {
      if (!Regex.IsMatch(msg.Content, ListCommandRegex))
        return;
      var regexMatch = Regex.Match(msg.Content, ListCommandRegex);

      string key = regexMatch.Groups["key"].Value;
      if (String.IsNullOrWhiteSpace(key))
        return;

      var listRoot = GetRootByKey(key);
      var delDict = new Dictionary<string, XElement>();

      RPItemDictGenerator(listRoot, $"{key}.", delDict);

      List<string> imgDeleted = new List<string>();

      foreach (var delKVP in delDict)
      {
        DeleteItemRecursively(delKVP.Value);
        imgDeleted.Add(delKVP.Key);
        try
        {
          await Task.Run(() => File.Delete(Path.Combine(_guildPath, _moduleFolder, delKVP.Value.Value))).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          ex.LogToConsole($"[{LogPref}][DEL] Image deletion failed: {key} -> {delKVP.Value.Value}");
          throw;
        }
      }

      if (imgDeleted.Count == 0)
      {
        await msg.Channel.SendMessageAsyncSafe($"Щооо ?? {EmojiCodes.WaitWhat}").ConfigureAwait(false);
        return;
      }

      await ModuleConfigChanged().ConfigureAwait(false);
      Reconfigure(_configEl);
      string output = $"Видалив наступні пікчі:{Environment.NewLine}```{Environment.NewLine}";

      await msg.Channel.GenerateAndSendOutputMessages(output,
                                                      imgDeleted,
                                                      s => $"{s}{Environment.NewLine}",
                                                      s => $"```{Environment.NewLine}{s}",
                                                      s => $"{s}```").ConfigureAwait(false);

      await msg.Channel.SendMessageAsyncSafe(EmojiCodes.Pepe).ConfigureAwait(false);
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
            new XAttribute("cfgPerm", String.Empty),
            new XAttribute("addPerm", String.Empty),
            new XAttribute("usePerm", String.Empty),
            new XAttribute("delPerm", String.Empty))).Save(filePath);
      }
      catch (Exception ex)
      {
        ex.LogToConsole($"[{LogPref}] Default config creation failed.");
        throw;
      }
    }
  }
}
