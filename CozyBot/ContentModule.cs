using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Globalization;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Discord.WebSocket;

namespace CozyBot
{
  /// <summary>
  /// Abstract Class describes user content, which can be saved and posted by bot.
  /// </summary>
  public abstract class ContentModule : IBotModule
  {
    /// <summary>
    /// Regex used in List commands parsing;
    /// </summary>
    private static string _listCommandRegex = @"^(?<pref>\S+)\s*(?<key>\S+)?$";

    /// <summary>
    /// Used for Discord message limit check.
    /// </summary>
    protected const int _msgLengthLimit = 1800;

    /// <summary>
    /// Configuration commands list.
    /// </summary>
    protected List<IBotCommand> _cfgCommands = new List<IBotCommand>();

    /// <summary>
    /// Content Use commands list.
    /// </summary>
    protected List<IBotCommand> _useCommands = new List<IBotCommand>();

    /// <summary>
    /// Content Addition commands list.
    /// </summary>
    protected List<IBotCommand> _addCommands = new List<IBotCommand>();

    /// <summary>
    /// Content deletion commands list.
    /// </summary>
    protected List<IBotCommand> _delCommands = new List<IBotCommand>();

    /// <summary>
    /// Boolean flag indicating if module is active.
    /// </summary>
    protected bool _isActive;

    /// <summary>
    /// Module's commands default prefix.
    /// </summary>
    protected static string _defaultPrefix = "c!";

    /// <summary>
    /// Module's commands current prefix.
    /// </summary>
    protected string _prefix;

    /// <summary>
    /// Bot's ID.
    /// </summary>
    protected ulong _clientId;

    /// <summary>
    /// List of guild admins IDs.
    /// </summary>
    protected List<ulong> _adminIds;

    /// <summary>
    /// XElement containing guild's modules config.
    /// </summary>
    protected XElement _configEl;

    /// <summary>
    /// XElement containing this module config.
    /// </summary>
    protected XDocument _moduleConfig;

    /// <summary>
    /// Internal RNG.
    /// </summary>
    protected static Random _rnd = new Random();

    /// <summary>
    /// Internal configuration change event.
    /// </summary>
    protected event ConfigChanged _configChanged;

    /// <summary>
    /// Lock object for module XML config file.
    /// </summary>
    protected object _moduleConfigLock = new object();

    /// <summary>
    /// Guild working path.
    /// </summary>
    protected string _guildPath;

    /// <summary>
    /// Regex used in Add command parsing.
    /// </summary>
    protected abstract string AddCommandRegex { get; }

    /// <summary>
    /// Regex used in List commands parsing;
    /// </summary>
    protected virtual string ListCommandRegex { get; } = _listCommandRegex;

    /// <summary>
    /// Configuration Changed Event. Raised on changes to config.
    /// </summary>
    public event ConfigChanged GuildBotConfigChanged
    {
      add
      {
        if (value != null)
          _configChanged += value;
      }
      remove
      {
        if (value != null)
          _configChanged -= value;
      }
    }

    public bool IsActive => _isActive;

    /// <summary>
    /// Module string identifier.
    /// </summary>
    public abstract string StringID { get; }

    /// <summary>
    /// Module identifier for logging purposes.
    /// </summary>
    public virtual string LogName => StringID.ToUpper(CultureInfo.InvariantCulture);

    /// <summary>
    /// Module XML config path.
    /// </summary>
    public abstract string ModuleConfigFilePath { get; }

    /// <summary>
    /// Module name in Guild config.
    /// </summary>
    public abstract string ModuleXmlName { get; }

    /// <summary>
    /// List containing active commands.
    /// </summary>
    public virtual IEnumerable<IBotCommand> ActiveCommands
    {
      get
      {
        foreach (var cmd in _cfgCommands)
        {
          yield return cmd;
        }
        foreach (var cmd in _addCommands)
        {
          yield return cmd;
        }
        foreach (var cmd in _useCommands)
        {
          yield return cmd;
        }
        foreach (var cmd in _delCommands)
        {
          yield return cmd;
        }
      }
    }

    /// <summary>
    /// ContentModule constructor.
    /// </summary>
    /// <param name="configEl">XElement containing guild's modules config.</param>
    /// <param name="adminIds">List of Guild Admins.</param>
    /// <param name="clientId">ID of Bot.</param>
    /// <param name="guildPath">Guild path.</param>
    public ContentModule(XElement configEl,
                         List<ulong> adminIds,
                         ulong clientId,
                         string guildPath)
    {
      _configEl = Guard.NonNull(configEl, nameof(configEl));
      _guildPath = Guard.NonNullWhitespaceEmpty(guildPath, nameof(guildPath));
      _adminIds = adminIds;
      _clientId = clientId;

      if (_configEl.Element(ModuleXmlName) == null)
      {
        XElement moduleConfigEl = new XElement(ModuleXmlName,
                                               new XAttribute("on", Boolean.FalseString),
                                               new XAttribute("prefix", _defaultPrefix),
                                               ModuleConfigFilePath);

        _configEl.Add(moduleConfigEl);
      }

      if (!Directory.Exists(_guildPath))
        Directory.CreateDirectory(_guildPath);

      Configure(_configEl);
    }

    /// <summary>
    /// Module reconfiguration method.
    /// </summary>
    /// <param name="configEl">XElement containing guild's modules config.</param>
    public void Reconfigure(XElement configEl)
    {
      if (configEl == null)
        return;

      Configure(configEl);
    }

    /// <summary>
    /// Extracts list of IDs from XAttribute.
    /// </summary>
    /// <param name="attr">XAttribute to extract IDs from.</param>
    /// <returns>List of IDs from specified XAttribute.</returns>
    protected List<ulong> ExtractPermissions(XAttribute attr)
    {
      List<ulong> ids = new List<ulong>();

      if (attr != null)
      {
        string permStringValue = attr.Value;
        string[] stringIds = permStringValue.Trim().Split(" ");
        if (stringIds.Length > 0)
          foreach (var stringID in stringIds)
            if (ulong.TryParse(stringID, out ulong id))
              ids.Add(id);
      }

      return ids;
    }

    /// <summary>
    /// Module configuration method.
    /// </summary>
    /// <param name="configEl">XElement containing guild's modules config.</param>
    protected virtual void Configure(XElement configEl)
    {
      XElement guildBotModuleCfg = configEl.Element(ModuleXmlName);

      bool isActive = false;

      if (guildBotModuleCfg.Attribute("on") != null &&
          !Boolean.TryParse(guildBotModuleCfg.Attribute("on").Value, out isActive))
        isActive = false;

      _isActive = isActive;
      string prefix = _defaultPrefix;

      if (guildBotModuleCfg.Attribute("prefix") != null &&
          !String.IsNullOrWhiteSpace(guildBotModuleCfg.Attribute("prefix").Value))
        prefix = guildBotModuleCfg.Attribute("prefix").Value;

      _prefix = prefix;

      if (!File.Exists(ModuleConfigFilePath))
        CreateDefaultModuleConfig(ModuleConfigFilePath);

      _moduleConfig = XDocument.Load(ModuleConfigFilePath);
      XElement moduleCfgEl = _moduleConfig.Root;

      if (moduleCfgEl.Attribute("cfgPerm") == null)
        moduleCfgEl.Add(new XAttribute("cfgPerm", ""));
      if (moduleCfgEl.Attribute("addPerm") == null)
        moduleCfgEl.Add(new XAttribute("addPerm", ""));
      if (moduleCfgEl.Attribute("usePerm") == null)
        moduleCfgEl.Add(new XAttribute("usePerm", ""));
      if (moduleCfgEl.Attribute("delPerm") == null)
        moduleCfgEl.Add(new XAttribute("delPerm", ""));

      List<ulong> addPermissionList = ExtractPermissions(moduleCfgEl.Attribute("addPerm"));
      List<ulong> usePermissionList = ExtractPermissions(moduleCfgEl.Attribute("usePerm"));
      List<ulong> cfgPermissionList = ExtractPermissions(moduleCfgEl.Attribute("cfgPerm"));
      List<ulong> delPermissionList = ExtractPermissions(moduleCfgEl.Attribute("delPerm"));

      if (isActive)
      {
        GenerateCfgCommands(cfgPermissionList);
        GenerateUseCommands(usePermissionList);
        GenerateAddCommands(addPermissionList);
        GenerateDelCommands(delPermissionList);
      }
      else
      {
        _cfgCommands = new List<IBotCommand>();
        _useCommands = new List<IBotCommand>();
        _addCommands = new List<IBotCommand>();
        _delCommands = new List<IBotCommand>();
      }
    }

    /// <summary>
    /// Creates default module XML config file.
    /// </summary>
    /// <param name="filePath">Path to config file.</param>
    protected abstract void CreateDefaultModuleConfig(string filePath);

    /// <summary>
    /// Generates module configuration commands from specified list of allowed role IDs.
    /// </summary>
    /// <param name="perms">List of Roles IDs which are allowed to use Config commands.</param>
    protected virtual void GenerateCfgCommands(List<ulong> perms)
    {
      List<ulong> allPerms = new List<ulong>(_adminIds);
      allPerms.AddRange(perms);
      Rule cmdRule = RuleGenerator.HasRoleByIds(allPerms) & RuleGenerator.PrefixatedCommand(_prefix, "cfg");

      IBotCommand configCmd = new BotCommand($"{StringID}-configcmd", cmdRule, ConfigCommand);

      _cfgCommands = new List<IBotCommand> { configCmd };
    }

    /// <summary>
    /// Generates content addition commands from specified list of allowed role IDs.
    /// </summary>
    /// <param name="perms">List of Roles IDs which are allowed to use Add commands.</param>
    protected virtual void GenerateAddCommands(List<ulong> perms)
    {
      List<ulong> allPerms = new List<ulong>(_adminIds);
      allPerms.AddRange(perms);
      Rule addRule = RuleGenerator.HasRoleByIds(allPerms) & RuleGenerator.PrefixatedCommand(_prefix, "add");

      IBotCommand addCmd = new BotCommand($"{StringID}-addcmd", addRule, AddCommand);

      _addCommands = new List<IBotCommand> { addCmd };
    }

    /// <summary>
    /// Generates content deletion commands from specified list of allowed role IDs.
    /// </summary>
    /// <param name="perms">List of Roles IDs which are allowed to use Delete commands.</param>
    protected virtual void GenerateDelCommands(List<ulong> perms)
    {
      List<ulong> allPerms = new List<ulong>(_adminIds);
      allPerms.AddRange(perms);
      Rule delRule = RuleGenerator.HasRoleByIds(allPerms) & RuleGenerator.PrefixatedCommand(_prefix, "del");

      IBotCommand delCmd = new BotCommand($"{StringID}-delcmd", delRule, DeleteCommand);

      _delCommands = new List<IBotCommand> { delCmd };
    }

    /// <summary>
    /// Generates content usage commands from specified list of allowed role IDs.
    /// </summary>
    /// <param name="perms">List of Roles IDs which are allowed to use Use commands.</param>
    protected virtual void GenerateUseCommands(List<ulong> perms)
    {
      List<IBotCommand> useCommands = new List<IBotCommand>();

      var stringKeys = RPKeyListGenerator(_moduleConfig.Root, String.Empty, true);

      List<ulong> allUsePerms = new List<ulong>(_adminIds);
      allUsePerms.AddRange(perms);
      Rule useRule;
      foreach (var strKey in stringKeys)
      {
        useRule = RuleGenerator.HasRoleByIds(allUsePerms) &
                  RuleGenerator.PrefixatedCommand(_prefix, strKey) &
                  !RuleGenerator.UserByID(_clientId); // prevent bot triggering on itself

        useCommands.Add(new BotCommand($"{StringID}-{strKey}-usecmd", useRule, UseCommandGenerator(strKey)));
      }

      useRule = RuleGenerator.HasRoleByIds(allUsePerms) &
                RuleGenerator.TextIdentity(_prefix) &
                !RuleGenerator.UserByID(_clientId);

      // to support empty prefix commands e.g. c! or i!
      useCommands.Add(new BotCommand($"{StringID}-usecmd", useRule, UseCommandGenerator(String.Empty)));

      List<ulong> allHelpPerms = new List<ulong>(_adminIds);
      allHelpPerms.AddRange(perms);
      Rule helpRule = RuleGenerator.HasRoleByIds(allHelpPerms) & RuleGenerator.PrefixatedCommand(_prefix, "help");

      useCommands.Add(new BotCommand($"{StringID}-helpcmd", helpRule, HelpCommand));

      List<ulong> allListPerms = new List<ulong>(_adminIds);
      allListPerms.AddRange(perms);
      Rule listRule = RuleGenerator.HasRoleByIds(allListPerms) & RuleGenerator.PrefixatedCommand(_prefix, "list");

      useCommands.Add(new BotCommand($"{StringID}-listcmd", listRule, ListCommand));

      List<ulong> allSearchPerms = new List<ulong>(_adminIds);
      allSearchPerms.AddRange(perms);
      Rule searchRule = RuleGenerator.HasRoleByIds(allSearchPerms) & RuleGenerator.PrefixatedCommand(_prefix, "search");

      useCommands.Add(new BotCommand($"{StringID}-searchcmd", searchRule, SearchCommand));

      _useCommands = useCommands;
    }

    /// <summary>
    /// Recursive Prefixated Key Generator.
    /// </summary>
    /// <param name="el">Element to search keys in.</param>
    /// <param name="prev">Previous prefix.</param>
    /// <returns>List of strings - valid prefixes for commands.</returns>
    protected virtual List<string> RPKeyListGenerator(XElement el,
                                                      string prev,
                                                      bool includeItems)
    {
      List<string> results = new List<string>();
      if (includeItems)
        results.AddRange(RPKLGenStubs(el, prev));

      foreach (var key in el.Elements("key"))
        if (key.Attribute("name") != null)
        {
          string prefix = prev + key.Attribute("name").Value;
          results.Add(prefix);
          results.AddRange(RPKeyListGenerator(key, prefix + ".", includeItems));
        }

      return results;
    }

    /// <summary>
    /// Recursive Prefixated Key Generator Item Stubs.
    /// </summary>
    /// <param name="el">Element to search for item stubs.</param>
    /// <param name="prev">Previous prefix.</param>
    /// <returns>List of strings - valid item references.</returns>
    protected virtual List<string> RPKLGenStubs(XElement el, string prev)
    {
      List<string> results = new List<string>();

      foreach (var item in el.Elements("item"))
      {
        var temp = item.Attribute("name");
        if (temp != null && !String.IsNullOrWhiteSpace(temp.Value))
          results.Add(prev + temp.Value);
      }

      return results;
    }

    protected virtual void RPItemDictGenerator(XElement el,
                                               string prev,
                                               Dictionary<string, XElement> dict)
    {
      RPIDGenStub(el, prev, dict);

      foreach (var key in el.Elements("key"))
      {
        if (key.Attribute("name") != null)
        {
          string prefix = prev + key.Attribute("name").Value;
          RPItemDictGenerator(key, prefix + ".", dict);
        }
      }
    }

    protected virtual void RPIDGenStub(XElement el,
                                      string prev,
                                      Dictionary<string, XElement> dict)
    {
      // deletion hotfix
      // TODO : rewrite or enhance

      if (el.Name == "item")
      {
        dict.Add(prev.TrimEnd('.'), el);
        // return; ?
      }
      foreach (var item in el.Elements("item"))
      {
        var temp = item.Attribute("name");
        if (temp != null && !String.IsNullOrWhiteSpace(temp.Value))
          dict.Add(prev + temp.Value, item);
      }
    }

    protected virtual XElement GetRootByKey(string key)
    {
      XElement currentKeyEl = _moduleConfig.Root;

      if (String.IsNullOrEmpty(key))
        return currentKeyEl;

      string[] keys = key.Split('.');

      foreach (var subKey in keys)
      {
        XElement subEl = GetFirstOrDefaultSubElementByName(currentKeyEl, "key", subKey) ??
                         GetFirstOrDefaultSubElementByName(currentKeyEl, "item", subKey);

        if (subEl == null)
          return null;

        currentKeyEl = subEl;
      }
      return currentKeyEl;
    }

    protected virtual List<XElement> GetItemsListByKey(string key)
    {
      var root = GetRootByKey(key);
      return (root != null) ? GetItemsByRoot(root) : new List<XElement>();
    }

    /// <summary>
    /// Method for items extraction from tree.
    /// </summary>
    /// <param name="root">Root element from which to extract all items.</param>
    /// <returns>List of all items in tree.</returns>
    private List<XElement> GetItemsByRoot(XElement root)
    {
      List<XElement> result = new List<XElement>();
      if (root.Name == "item")
        result.Add(root);
      foreach (var el in root.Elements("item"))
        result.Add(el);
      foreach (var key in root.Elements("key"))
        result.AddRange(GetItemsByRoot(key));
      return result;
    }

    protected virtual void DeleteItemRecursively(XElement el)
    {
      XElement parentEl = el.Parent;
      foreach (var element in parentEl.Elements("item"))
        if (element.Attribute("name") != el.Attribute("name"))
        {
          el.Remove();
          return;
        }
      foreach (var element in parentEl.Elements("key"))
        if (element.Attribute("name") != el.Attribute("name"))
        {
          el.Remove();
          return;
        }
      if (parentEl.Name == ModuleXmlName)
        el.Remove();
      else
      {
        DeleteItemRecursively(parentEl);
        el.Remove();
      }
    }

    /// <summary>
    /// Module configuration command.
    /// </summary>
    /// <param name="msg">SocketMessage containing command.</param>
    /// <returns>Async Task performing configuration.</returns>
    protected virtual async Task ConfigCommand(SocketMessage msg)
    {
      string content = msg.Content;
      string[] words = content.Split(" ");
      if (words.Length > 3)
        switch (words[1])
        {
          case "perm":
            await PermissionControlCommand(words[2], msg).ConfigureAwait(false);
            break;
        }
    }

    protected virtual async Task SearchCommand(SocketMessage msg)
    {
      string cmdPrefix = $"[{LogName}][SEARCH]";
      BotHelper.LogDebugToConsole($"{cmdPrefix} Entered search.");
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

        Regex regex;
        try
        {
          regex = new Regex(regexStr, RegexOptions.CultureInvariant);
        }
        catch (ArgumentException ex)
        {
          ex.LogToConsole($"[WARNING]{cmdPrefix} Malformed regex \"{regexStr}\".");
          await msg.Channel.SendMessageAsyncSafe($"Що це за хуйня?? {regexStr} {EmojiCodes.Tomas}").ConfigureAwait(false);
          return;
        }

        var matchedKeysList = RPKeyListGenerator(GetRootByKey(String.Empty), String.Empty, true).Where(key => regex.IsMatch(key)).ToList();

        BotHelper.LogDebugToConsole($"{cmdPrefix} Number of matches: {matchedKeysList.Count} for regex \"{regexStr}\".");

        if (!matchedKeysList.Any())
        {
          await msg.Channel.SendMessageAsyncSafe($"Не знайдено **ключів** за запитом `{regexStr}` {EmojiCodes.Pepe}").ConfigureAwait(false);
          return;
        }
        await msg.Channel.SendMessageAsyncSafe($"Знайдено {matchedKeysList.Count} **ключів** за запитом `{regexStr}` {EmojiCodes.DankPepe}").ConfigureAwait(false);

        string output = $"Результати пошуку **ключів** за запитом `{regexStr}`:{Environment.NewLine}```";
        List<string> outputMsgs = new List<string>();
        foreach (var key in matchedKeysList)
        {
          if (output.Length + key.Length < _msgLengthLimit)
            output = String.Concat(output, $"{Environment.NewLine}{key}");
          else
          {
            output = String.Concat(output, "```");
            outputMsgs.Add(output);
            output = $"```{Environment.NewLine}{key}";
          }
        }
        output = String.Concat(output, "```");
        outputMsgs.Add(output);

        var dm = await msg.Author.GetOrCreateDMChannelAsync().ConfigureAwait(false);
        foreach (var message in outputMsgs)
          await dm.SendMessageAsyncSafe(message).ConfigureAwait(false);
        //await msg.Channel.SendMessageAsyncSafe($"{msg.Author.Mention} подивись в приватні повідомлення {EmojiCodes.Bumagi}").ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        ex.LogToConsole($"[WARNING]{cmdPrefix} Command failed. Query=\"{regexStr}\"");
        throw;
      }
    }

    /// <summary>
    /// Permission control command. Changes commands usage permissions on per-role basis.
    /// </summary>
    /// <param name="category">Command to change access to. Valid values - cfg, add, use, del.</param>
    /// <param name="msg">SocketMessage containing command.</param>
    /// <returns>Async Task perfrorming permissions change.</returns>
    protected virtual async Task PermissionControlCommand(string category, SocketMessage msg)
    {
      var roles = msg.MentionedRoles;
      List<ulong> rolesIds = new List<ulong>();

      foreach (var role in roles)
        rolesIds.Add(role.Id);

      switch (category)
      {
        case "cfg":
          await ModifyPermissions(_moduleConfig.Root.Attribute("cfgPerm"), rolesIds).ConfigureAwait(false);
          GenerateCfgCommands(rolesIds);
          break;
        case "add":
          await ModifyPermissions(_moduleConfig.Root.Attribute("addPerm"), rolesIds).ConfigureAwait(false);
          GenerateAddCommands(rolesIds);
          break;
        case "use":
          await ModifyPermissions(_moduleConfig.Root.Attribute("usePerm"), rolesIds).ConfigureAwait(false);
          GenerateUseCommands(rolesIds);
          break;
        case "del":
          await ModifyPermissions(_moduleConfig.Root.Attribute("delPerm"), rolesIds).ConfigureAwait(false);
          GenerateDelCommands(rolesIds);
          break;
      }

      await msg.Channel.SendMessageAsyncSafe($"Дозволи було змінено {EmojiCodes.Picardia}").ConfigureAwait(false);
    }

    /// <summary>
    /// Modifies access to specified command, allowing specified role IDs usage of command.
    /// </summary>
    /// <param name="attr">XAttribute specifing permission.</param>
    /// <param name="ids">List of Role IDs allowed to use command.</param>
    /// <returns>Async Task performing permissions change.</returns>
    protected virtual async Task ModifyPermissions(XAttribute attr, List<ulong> ids)
    {
      string newValue = String.Empty;

      foreach (var id in ids)
        newValue += $"{id} ";

      attr.Value = newValue;

      await ModuleConfigChanged().ConfigureAwait(false);
    }

    /// <summary>
    /// Saves Module XML Config File.
    /// </summary>
    /// <returns></returns>
    protected virtual async Task ModuleConfigChanged()
      => await Task.Run(() =>
      {
        lock (_moduleConfigLock)
          _moduleConfig.Save(ModuleConfigFilePath);
      }).ConfigureAwait(false);

    /// <summary>
    /// GuildBot Config Changed Event wrapper.
    /// </summary>
    /// <param name="configEl">New configuration.</param>
    /// <returns>Async task performing config change.</returns>
    protected async Task RaiseConfigChanged(XElement configEl)
      => await Task.Run(() => _configChanged(this, new ConfigChangedEventArgs(configEl))).ConfigureAwait(false);

    /// <summary>
    /// Abstract method for content Addition command.
    /// </summary>
    /// <param name="msg">Message containing command invocation.</param>
    /// <returns>Async Task performing content Addition.</returns>
    protected abstract Task AddCommand(SocketMessage msg);

    /// <summary>
    /// Abstract method for content Addition command.
    /// </summary>
    /// <param name="msg">Message containing command invocation.</param>
    /// <returns>Async Task performing content Deletion.</returns>
    protected abstract Task DeleteCommand(SocketMessage msg);

    /// <summary>
    /// Abstract method for Help command.
    /// </summary>
    /// <param name="msg">Message containing command invocation.</param>
    /// <returns>Async Task performing Help function.</returns>
    protected abstract Task HelpCommand(SocketMessage msg);

    /// <summary>
    /// Abstract method for providing List of user uploaded content.
    /// </summary>
    /// <param name="msg">Message containing command invocation.</param>
    /// <returns>Async Task performing List function.</returns>
    protected abstract Task ListCommand(SocketMessage msg);

    /// <summary>
    /// Abstract method for content Usage command generator.
    /// </summary>
    /// <param name="key">Key by which content should be accessed.</param>
    /// <returns>Function which provides content accessed by specified key.</returns>
    protected abstract Func<SocketMessage, Task> UseCommandGenerator(string key);

    private static XElement GetFirstOrDefaultSubElementByName(XElement rootEl, string elName, string subKey)
      => rootEl.Elements(elName).FirstOrDefault(
        el => el.Attribute("name") != null && subKey.ExactAs(el.Attribute("name").Value));
  }
}
