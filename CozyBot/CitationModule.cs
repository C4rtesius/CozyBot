using System;
using System.IO;
using System.Linq;
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
    protected string[] _blacklistedKeys =
    {
      "add",
      "list",
      "vlist",
      "help",
      "cfg",
      "del",
      "search"
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
      => String.IsNullOrEmpty(_moduleConfigFilePath) ?
         _moduleConfigFilePath = Path.Combine(_guildPath, _configFileName) :
         _moduleConfigFilePath;

    /// <summary>
    /// Citation module constructor.
    /// </summary>
    /// <param name="configEl">XML Element containing Guild modules config.</param>
    /// <param name="adminIds">IDs of Guild admins.</param>
    /// <param name="clientId">Bot ID.</param>
    /// <param name="workingPath">Path to module working folder.</param>
    public CitationModule(XElement configEl, List<ulong> adminIds, ulong clientId, string workingPath)
        : base(configEl, adminIds, clientId, workingPath)
    {
      if (!Directory.Exists(Path.Combine(_guildPath, _moduleFolder)))
        Directory.CreateDirectory(Path.Combine(_guildPath, _moduleFolder));

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
        string logPrefix = $"[{LogPref}][USE][key={key}]";
        await msg.DeleteAsyncSafe(logPrefix).ConfigureAwait(false);

        string dictKey = $"{msg.Author.Id}{msg.Channel.Id}{key}";
        if (_ratelimitDict.ContainsKey(dictKey))
          return;
        _ratelimitDict.TryAdd(dictKey, Task.Run(async () =>
          { await Task.Delay(10000).ConfigureAwait(false); _ratelimitDict.TryRemove(dictKey, out _); }));

        var citationsList = GetItemsListByKey(key);
        if (citationsList.Count == 0)
          return;

        XElement citationEl = citationsList[_rnd.Next() % citationsList.Count];
        string citationFileName = citationEl.Value;

        string citation;
        try
        {
          citation = await File.ReadAllTextAsync(Path.Combine(_guildPath, _moduleFolder, citationFileName)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          ex.LogToConsole($"{logPrefix} Citation retrieval failed.");
          throw;
        }

        if (citation.Length > _msgLengthLimit) // in a case citation is longer than limit - ignore it
          return;

        try
        {
          await SendTextMessageHumanLike(msg, citation).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          ex.LogToConsole($"{logPrefix} Citation send failed.");
          throw;
        }

        // Increment usage count.

        var usageAttribute = citationEl.Attribute(_usageCountAttributeName);
        if (usageAttribute != null)
          usageAttribute.Value = Int32.TryParse(usageAttribute.Value, out int uses) ? $"{++uses}" : "1";
        else
          citationEl.Add(new XAttribute(_usageCountAttributeName, "1"));

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

      int totalDelay = 500 + line.Length * 50 + (_rnd.Next() % 2000);
      for (; totalDelay > 5000; totalDelay -= 5000)
      {
        await Task.Delay(5000).ConfigureAwait(false);
        await msg.Channel.TriggerTypingAsync().ConfigureAwait(false);
      }
      await Task.Delay(totalDelay).ConfigureAwait(false);

      await msg.Channel.SendMessageAsyncSafe(line).ConfigureAwait(false);
    }

    /// <summary>
    /// Command performing citation addition logic.
    /// </summary>
    /// <param name="msg">Message which invoked this command, containing citation to save.</param>
    /// <returns>Async Task performing citation addition logic.</returns>
    protected override async Task AddCommand(SocketMessage msg)
    {
      string logPrefix = $"[{LogPref}][ADD]";
      await msg.DeleteAsyncSafe(logPrefix).ConfigureAwait(false);

      var regexMatch = Regex.Match(msg.Content, AddCommandRegex);

      if (!regexMatch.Success)
        return;

      string[] keys = regexMatch.Groups["key"].Value.Split('.');

      if (_blacklistedKeys.Any(blKey => blKey.ExactAs(keys[0])))
        return;

      Guid newItemGuid = Guid.NewGuid();
      string newItemFileName = $"{newItemGuid}.dat";

      try
      {
        await File.WriteAllTextAsync(Path.Combine(_guildPath, _moduleFolder, newItemFileName), regexMatch.Groups["content"].Value).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        ex.LogToConsole($"{logPrefix} Citation save failed: {msg.Content}");
        throw;
      }

      XElement newItem = new XElement("item",
                                      new XAttribute("name", $"{newItemGuid}"),
                                      newItemFileName);

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
        ex.LogToConsole($"{logPrefix} Config save failed.");
        throw;
      }

      Reconfigure(_configEl);

      await msg.Channel.SendMessageAsyncSafe($"Записав цитатку {EmojiCodes.DankPepe}").ConfigureAwait(false);
    }

    protected override void GenerateUseCommands(List<ulong> perms)
    {
      // Generate base use commands from ContentModule
      base.GenerateUseCommands(perms);

      // Add verbose listing command
      List<ulong> allListPerms = new List<ulong>(_adminIds);
      allListPerms.AddRange(perms);
      Rule vlistRule = RuleGenerator.HasRoleByIds(allListPerms) & RuleGenerator.PrefixatedCommand(_prefix, "vlist");

      _useCommands.Add(new BotCommand($"{StringID}-vlistcmd", vlistRule, VerboseListCommand));
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

      string output = String.Concat("**Список доступних цитат",
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

    protected override async Task SearchCommand(SocketMessage msg)
    {
      string cmdPrefix = $"[{LogPref}][SEARCH-FILES]";
      BotHelper.LogDebugToConsole($"{cmdPrefix} Entering.");
      var regexStr = msg.Content.Replace($"{_prefix}search", String.Empty, StringComparison.InvariantCulture).TrimStart();
      try
      {
        if (regexStr.Length == 0)
          return;
        if (regexStr.Length > 200) // unreasonably long regex
        {
          await msg.Channel.SendMessageAsyncSafe($"Занадто довгий запит: `{regexStr}` {EmojiCodes.WaitWhat}").ConfigureAwait(false);
          return;
        }

        // init regex before starting waiter, as regex can be malformed
        Regex regex;
        try
        {
          regex = new Regex(regexStr, RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.IgnoreCase);
        }
        catch (ArgumentException ex)
        {
          ex.LogToConsole($"[WARNING]{cmdPrefix} Malformed regex \"{regexStr}\".");
          await msg.Channel.SendMessageAsyncSafe($"Що це за хуйня?? {EmojiCodes.Tomas} `{regexStr}`").ConfigureAwait(false);
          return;
        }

        using ManualResetEventSlim mres = new ManualResetEventSlim(false);

        var waitTask = Task.Run(async () =>
        {
          BotHelper.LogDebugToConsole($"{cmdPrefix} Entering waiter.");
          var waitMsg = await msg.Channel.SendMessageAsyncSafe($"Шукаю `{regexStr}` в цитатах {EmojiCodes.Bumagi}").ConfigureAwait(false);
          if (waitMsg == null)
            return;
          string content = String.Empty;
          while (mres != null && !mres.IsSet)
          {
            await Task.Delay(5000).ConfigureAwait(false);
            content = (await msg.Channel.GetMessageAsync(waitMsg.Id).ConfigureAwait(false)).Content;
            await waitMsg.ModifyAsync(p => { p.Content = $"{content}{EmojiCodes.Bumagi}"; }).ConfigureAwait(false);
          }
          content = (await msg.Channel.GetMessageAsync(waitMsg.Id).ConfigureAwait(false)).Content;
          await waitMsg.ModifyAsync(p => { p.Content = $"{content}{Environment.NewLine}Пошук закінчено. {EmojiCodes.Picardia}"; }).ConfigureAwait(false);
        });

        var itemsDict = new Dictionary<string, XElement>();
        var matchesDict = new Dictionary<string, string>();
        RPItemDictGenerator(GetRootByKey(String.Empty), String.Empty, itemsDict);
        BotHelper.LogDebugToConsole($"{cmdPrefix} {itemsDict.Count} entries in dictionary.");

        foreach (var kvp in itemsDict)
        {
          string citation;
          string citationFileName = kvp.Value.Value;

          try
          {
            citation = await File.ReadAllTextAsync(Path.Combine(_guildPath, _moduleFolder, citationFileName)).ConfigureAwait(false);
          }
          catch (Exception ex)
          {
            ex.LogToConsole($"[WARNING]{cmdPrefix} Citation loading failed: {kvp.Key} - {kvp.Value.Value}");
            continue;
          }

          if (citation.Length > _msgLengthLimit || !regex.IsMatch(citation))
            continue;
          matchesDict.Add(kvp.Key, citation);
        }

        BotHelper.LogDebugToConsole($"{cmdPrefix} {matchesDict.Count} matches.");

        mres.Set();
        await Task.WhenAll(waitTask).ConfigureAwait(false);

        if (matchesDict.Count == 0)
        {
          await msg.Channel.SendMessageAsyncSafe($"Не знайдено **цитат** за запитом `{regexStr}` {EmojiCodes.Pepe}").ConfigureAwait(false);
          return;
        }
        await msg.Channel.SendMessageAsyncSafe($"Знайдено {matchesDict.Count} **цитат** за запитом `{regexStr}` {EmojiCodes.DankPepe}").ConfigureAwait(false);

        string output = $"Результати пошуку **цитат** за запитом: `{regexStr}`:{Environment.NewLine}";

        var dm = await msg.Author.GetOrCreateDMChannelAsync().ConfigureAwait(false);
        await dm.GenerateAndSendOutputMessages(output,
                                               matchesDict,
                                               kvp => $"`{kvp.Key}`{Environment.NewLine}{kvp.Value}{Environment.NewLine}",
                                               s => s,
                                               s => s).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        ex.LogToConsole($"[WARNING]{cmdPrefix} Search failed for regex \"{regexStr}\".");
        throw;
      }
      finally
      {
        BotHelper.LogDebugToConsole($"{cmdPrefix} Passing control to keys search.");
        await base.SearchCommand(msg).ConfigureAwait(false);
      }
    }

    protected virtual async Task VerboseListCommand(SocketMessage msg)
    {
      string cmdPrefix = $"[{LogPref}][VLIST]";
      await msg.DeleteAsyncSafe(cmdPrefix).ConfigureAwait(false);

      var regexMatch = Regex.Match(msg.Content, ListCommandRegex);

      if (!regexMatch.Success)
        return;

      var cmdKey = regexMatch.Groups["key"].Value;

      var listRoot = GetRootByKey(cmdKey);
      var itemsDict = new Dictionary<string, XElement>();

      RPItemDictGenerator(listRoot, String.IsNullOrWhiteSpace(cmdKey) ? String.Empty : cmdKey + ".", itemsDict);

      List<string> outputMsgs = new List<string>();

      string output = String.Concat("**Розширений список цитат",
                                    String.IsNullOrWhiteSpace(cmdKey) ? String.Empty : @$" за ключем `{cmdKey}`",
                                    $":**{Environment.NewLine}");

      var dm = await msg.Author.GetOrCreateDMChannelAsync().ConfigureAwait(false);

      await dm.GenerateAndSendOutputMessages(output,
                                             itemsDict,
                                             getCitation,
                                             s => s,
                                             s => s).ConfigureAwait(false);

      await msg.Channel.SendMessageAsyncSafe($"{msg.Author.Mention} подивись в приватні повідомлення {EmojiCodes.Bumagi}").ConfigureAwait(false);

      async Task<string> getCitation(KeyValuePair<string, XElement> kvp)
      {
        try
        {
          string result = await File.ReadAllTextAsync(Path.Combine(_guildPath, _moduleFolder, kvp.Value.Value)).ConfigureAwait(false);
          if (result.Length > _msgLengthLimit)
            return String.Empty;
          return $"`{kvp.Key}`{Environment.NewLine}{result}{Environment.NewLine}";
        }
        catch (Exception ex)
        {
          ex.LogToConsole($"{cmdPrefix} Citation loading failed: {kvp.Value.Value}");
          return String.Empty;
        }
      }
    }

    protected override async Task HelpCommand(SocketMessage msg)
    {
      if (!(msg.Author is SocketGuildUser user))
        return;

      await msg.DeleteAsyncSafe($"[{LogPref}][HELP]").ConfigureAwait(false);

      //var dm = await msg.Author.GetOrCreateDMChannelAsync().ConfigureAwait(false);
      await msg.Channel.SendMessageAsync(String.Empty, false, BuildHelpEmbed(user)).ConfigureAwait(false);

      //await msg.Channel.SendMessageAsyncSafe($"{msg.Author.Mention} подивись в приватні повідомлення {EmojiCodes.Bumagi}").ConfigureAwait(false);
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
        Name = "Команди цитатного модуля",
        Value = String.Join(Environment.NewLine,
                            @$"`{_prefix}cfg perm [use/add/del/cfg] @Роль1 @Роль2 ...` - налаштування доступу до команд",
                            @$"`{_prefix}add ключ цитата` - зберегти цитату з ключем",
                            $@"`{_prefix}search запит` - знайти цитату по ключу або змісту цитат за запитом",
                            @$"`{_prefix}vlist [ключ]` - отримати список цитат за ключем",
                            @$"`{_prefix}list [ключ]` - отримати список підключів за ключем",
                            @$"`{_prefix}del ключ` - видалити ключ та пов'язані з ним цитати",
                            @$"`{_prefix}ключ` - отримати цитату за ключем",
                            @$"`{_prefix}help` - цей список команд",
                            @$"`{_prefix}` - отримати випадкову цитату")
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
      return eb.Build();
    }

    protected override async Task DeleteCommand(SocketMessage msg)
    {
      var regexMatch = Regex.Match(msg.Content, ListCommandRegex);

      if (!regexMatch.Success)
        return;

      string key = regexMatch.Groups["key"].Value;
      if (String.IsNullOrWhiteSpace(key))
        return;

      var delDict = new Dictionary<string, XElement>();
      RPItemDictGenerator(GetRootByKey(key), $"{key}.", delDict);
      List<string> citationsDeleted = new List<string>();

      foreach (var delKVP in delDict)
      {
        DeleteItemRecursively(delKVP.Value);
        citationsDeleted.Add(delKVP.Key);
        try
        {
          await Task.Run(() => File.Delete(Path.Combine(_guildPath, _moduleFolder, delKVP.Value.Value))).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          ex.LogToConsole($"[{LogPref}][DEL] Citation deletion failed: {key} -> {delKVP.Value.Value}");
          throw;
        }
      }

      if (citationsDeleted.Count == 0)
      {
        await msg.Channel.SendMessageAsyncSafe(@$"Щооо ?? {EmojiCodes.WaitWhat}").ConfigureAwait(false);
        return;
      }

      await ModuleConfigChanged().ConfigureAwait(false);
      Reconfigure(_configEl);
      string output = @$"Видалив наступні цитати:{Environment.NewLine}```{Environment.NewLine}";

      await msg.Channel.GenerateAndSendOutputMessages(output,
                                                      citationsDeleted,
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
        new XDocument(new XElement(ModuleXmlName,
                                   new XAttribute("cfgPerm", String.Empty),
                                   new XAttribute("addPerm", String.Empty),
                                   new XAttribute("usePerm", String.Empty),
                                   new XAttribute("delPerm", String.Empty))).Save(filePath);
      }
      catch (Exception ex)
      {
        ex.LogToConsole($"[{LogPref}] Default config creation failed: {filePath}");
        throw;
      }
    }
  }
}
