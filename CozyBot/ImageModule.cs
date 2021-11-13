using System;
using System.IO;
using System.Xml.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
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
      //"vlist", not implemented yet
      "help",
      "cfg",
      "del"
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
    {
      get
      {
        if (String.IsNullOrEmpty(_moduleConfigFilePath))
          _moduleConfigFilePath = Path.Combine(_guildPath, _configFileName);
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
        Directory.CreateDirectory(Path.Combine(_guildPath, _moduleFolder));

      _ratelimitDict = new ConcurrentDictionary<string, Task>();
    }

    protected override Func<SocketMessage, Task> UseCommandGenerator(string key)
    {
      return async (msg) =>
      {
        try
        {
          await msg.DeleteAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          BotHelper.LogExceptionToConsole($"[{_stringID.ToUpper()}] Command call deletion failed: {key}", ex);
          throw;
        }
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
          BotHelper.LogExceptionToConsole($"[{_stringID.ToUpper()}] File send failed: {key}", ex);
          throw;
        }

        if (imgEl.Attribute(_usageCountAttributeName) != null)
        {
          if (Int32.TryParse(imgEl.Attribute(_usageCountAttributeName).Value, out int uses))
          {
            uses++;
            imgEl.Attribute(_usageCountAttributeName).Value = $"{uses}";
          }
          else
            imgEl.Attribute(_usageCountAttributeName).Value = "1";
        }
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
        await BotHelper.SendMessageAsyncSafe(sc, $"Пікча з такою назвою вже є {EmojiCodes.Tomas}").ConfigureAwait(false);
        return;
      }

      using (HttpClient hc = new HttpClient())
        using (FileStream fs = new FileStream(filepath, FileMode.Create, FileAccess.Write))
          await (await hc.GetAsync(new Uri(att.Url)).ConfigureAwait(false)).Content.CopyToAsync(fs).ConfigureAwait(false);

      await BotHelper.SendMessageAsyncSafe(sc, "Зберіг пікчу " + EmojiCodes.DankPepe).ConfigureAwait(false);
    }

    protected override async Task AddCommand(SocketMessage msg)
    {
      if (!Regex.IsMatch(msg.Content, AddCommandRegex))
        return;

      var regexMatch = Regex.Match(msg.Content, AddCommandRegex);

      string[] keys = regexMatch.Groups["key"].Value.Split('.');

      foreach (var blKey in _blacklistedKeys) // check for blacklisted keys
        if (String.Compare(keys[0], blKey, StringComparison.InvariantCulture) == 0)
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
        BotHelper.LogExceptionToConsole($"[{_stringID.ToUpper()}] File download failed: {regexMatch.Groups["key"].Value}", ex);
        throw;
      }

      XElement currentEl = _moduleConfig.Root;

      foreach (var key in keys)
      {
        XElement newEl = currentEl.Elements("key").FirstOrDefault(el =>
          el.Attribute("name") != null && String.Compare(el.Attribute("name").Value, key, StringComparison.InvariantCulture) == 0);

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
        Console.WriteLine(String.Join(Environment.NewLine,
                                      $"[EXCEPT][{_stringID.ToUpper()}] Config save failed: {msg.Content}",
                                      $"Exception caught: {ex.Message}",
                                      $"Stack trace: {ex.StackTrace}"));
        throw;
      }

      Reconfigure(_configEl);

      try
      {
        await msg.DeleteAsync().ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        BotHelper.LogExceptionToConsole($"[{_stringID.ToUpper()}] Command call deletion failed: {msg.Channel.Name}", ex);
        throw;
      }
    }

    protected override async Task ListCommand(SocketMessage msg)
    {
      // TODO : fix a bug with wrong `list` command output
      try
      {
        await msg.DeleteAsync().ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        BotHelper.LogExceptionToConsole($"[{_stringID.ToUpper()}] Command call deletion failed: {msg.Channel.Name}", ex);
        throw;
      }

      // TODO : fix `c!list key` when key contains only 1 item

      if (!Regex.IsMatch(msg.Content, ListCommandRegex))
        return;

      var regexMatch = Regex.Match(msg.Content, ListCommandRegex);

      string keyStr = regexMatch.Groups["key"].Value;
      var listRoot = GetRootByKey(keyStr);

      List<string> outputMsgs = new List<string>();

      string output = String.Concat("**Список доступних пікч",
                                    String.IsNullOrWhiteSpace(keyStr) ? String.Empty : $" по підключу `{keyStr}`",
                                    ":**",
                                    Environment.NewLine,
                                    "```");

      var list = RPKeyListGenerator(listRoot,
                                    String.IsNullOrWhiteSpace(keyStr) ? "" : keyStr + ".",
                                    false);
      if (list.Count == 0)
        return;

      list.Add(keyStr);
      foreach (var key in list)
      {
        if (output.Length + key.Length < _msgLengthLimit)
          output += $"{Environment.NewLine}{key}";
        else
        {
          output += "```";
          outputMsgs.Add(output);
          output = $"```{key}";
        }
      }

      output += "```";
      outputMsgs.Add(output);

      var ch = await msg.Author.GetOrCreateDMChannelAsync().ConfigureAwait(false);

      foreach (var outputMsg in outputMsgs)
        await ch.SendMessageAsync(outputMsg).ConfigureAwait(false);

      output = $"{msg.Author.Mention} подивись в приватні повідомлення {EmojiCodes.Bumagi}";

      await BotHelper.SendMessageAsyncSafe(msg.Channel, output).ConfigureAwait(false);
    }

    protected override async Task HelpCommand(SocketMessage msg)
    {
      if (msg.Author is SocketGuildUser user)
      {
        try
        {
          await msg.DeleteAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          BotHelper.LogExceptionToConsole($"[{_stringID.ToUpper()}] Command call deletion failed: {msg.Channel.Name}", ex);
          throw;
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
          Value = String.Join(Environment.NewLine,
                              @$"{_prefix}cfg perm [use/add/del/cfg] @Роль1 @Роль2 ... - виставлення прав доступу до команд",
                              @$"{_prefix}add ключ [пікча] - додати пікчу з ключем",
                              @$"{_prefix}del ключ - видалити ключ та пов'язану з ним пікчу",
                              @$"{_prefix}list - отримати список доступних ключів у Приватних Повідомленнях",
                              @$"{_prefix}ключ - отримати пікчу за ключем",
                              @$"{_prefix} - отримати випадкову пікчу",
                              @$"{_prefix}help - цей список команд")
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

        var dm = await msg.Author.GetOrCreateDMChannelAsync().ConfigureAwait(false);
        await dm.SendMessageAsync(String.Empty, false, eb.Build()).ConfigureAwait(false);

        string output = $"{msg.Author.Mention} подивись в приватні повідомлення {EmojiCodes.Bumagi}";
        await BotHelper.SendMessageAsyncSafe(msg.Channel, output).ConfigureAwait(false);
      }
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
          BotHelper.LogExceptionToConsole($"[{_stringID.ToUpper()}] Image deletion failed: {key} -> {delKVP.Value.Value}", ex);
          throw;
        }
      }

      if (imgDeleted.Count == 0)
      {
        await BotHelper.SendMessageAsyncSafe(msg.Channel, $"Щооо ?? {EmojiCodes.WaitWhat}").ConfigureAwait(false);
        return;
      }

      await ModuleConfigChanged().ConfigureAwait(false);
      Reconfigure(_configEl);
      string output = $"Видалив наступні пікчі:{Environment.NewLine}```";
      foreach (var deleted in imgDeleted)
        output += $"{deleted}{Environment.NewLine}";
      output += $"```{Environment.NewLine}{EmojiCodes.Pepe}";

      await BotHelper.SendMessageAsyncSafe(msg.Channel, output).ConfigureAwait(false);
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
            new XAttribute("delPerm", ""))).Save(filePath);
      }
      catch (Exception ex)
      {
        BotHelper.LogExceptionToConsole($"[{_stringID.ToUpper()}] Default config creation failed.", ex);
        throw;
      }
    }
  }
}
