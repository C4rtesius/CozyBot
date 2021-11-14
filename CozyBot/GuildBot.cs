using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Discord;
using Discord.WebSocket;

namespace CozyBot
{
  public class GuildBot : IBot
  {
    private static readonly string[] _magic8BallResponses =
    {
      $"Це безсумнівно {EmojiCodes.Picardia}",
      $"Це безперечно так {EmojiCodes.Picardia}",
      $"Без сумнівів {EmojiCodes.Picardia}",
      $"Да, абсолютно {EmojiCodes.Picardia}",
      $"Можете покладатись на це {EmojiCodes.Picardia}",
      $"Наскільки я бачу - так {EmojiCodes.Picardia}",
      $"Скоріш за все {EmojiCodes.Picardia}",
      $"Так і є {EmojiCodes.Picardia}",
      $"Так {EmojiCodes.Picardia}",
      $"Все вказує на це {EmojiCodes.Picardia}",
      $"Відповідь туманна, спробуйте ще {EmojiCodes.Thonk}",
      $"Спитайте пізніше {EmojiCodes.Thonk}",
      $"Краще не відповідатиму зараз {EmojiCodes.Thonk}",
      $"Не можу передбачити {EmojiCodes.Thonk}",
      $"Сконцентруйтесь та спитайте знову {EmojiCodes.Thonk}",
      $"Не розраховуйте на це {EmojiCodes.BobRoss}",
      $"Я відповім - ні {EmojiCodes.BobRoss}",
      $"Мої джерела кажуть - ні {EmojiCodes.BobRoss}",
      $"Виглядає погано {EmojiCodes.BobRoss}",
      $"Дуже навряд {EmojiCodes.BobRoss}"
    };

    private Dictionary<string, IBotModule> _modulesDict;
    private List<IBotCommand> _commandsList;
    private XDocument _config;
    private string _guildPath;
    private string _configPath;
    private string _prefix;
    private List<ulong> _adminIds;
    private object _configLockObject = new object();
    private Random _rng = new Random(DateTime.Now.Millisecond);

    private ulong _clientId;
    private ulong _superUserID = 301809460803010560UL;

    private event CtrlEventHandler _ctrlEvent;

    protected event CtrlEventHandler CtrlEvent
    {
      add    { if (value != null) _ctrlEvent += value; }
      remove { if (value != null) _ctrlEvent -= value; }
    }

    protected SocketGuild _guild;

    public Dictionary<string, IBotModule> ModulesDict => _modulesDict;

    public GuildBot(SocketGuild guild, string guildPath, ulong clientId)
    {
      _guild = Guard.NonNull(guild, nameof(guild));
      _guildPath = Guard.NonNull(guildPath, nameof(guildPath));
      _configPath = Path.Combine(_guildPath, "config.xml");
      _clientId = clientId;

      if (!File.Exists(_configPath))
        throw new ApplicationException("Invalid configPath, file does not exist.");

      _adminIds = new List<ulong>();

      LoadConfig();

      _adminIds.AddRange(guild.Roles.Where(r => r.Permissions.Administrator).Select(r => r.Id));
      _modulesDict = new Dictionary<string, IBotModule>();

      LoadDefaultCommands();
      LoadModules();
    }

    public void Dispatch(SocketMessage msg)
    {
      foreach (var module in _modulesDict.Values)
        foreach (var cmd in module.ActiveCommands)
          if (cmd.CanExecute(msg))
          {
            cmd.ExecuteCommand(msg);
            return;
          }
      foreach (var cmd in _commandsList)
        if (cmd.CanExecute(msg))
        {
          cmd.ExecuteCommand(msg);
          return;
        }
    }

    public void OnCtrlEvent(CtrlType type)
    {
      if (_ctrlEvent != null)
        _ctrlEvent.Invoke(type);

      Task.Run(SaveConfig).GetAwaiter().GetResult();
    }

    private void LoadConfig()
    {
      BotHelper.LogDebugToConsole($"[GUILDBOT] _configPath: {_configPath}");
      BotHelper.LogDebugToConsole($"[GUILDBOT] Exists(_configPath): {File.Exists(_configPath)}");
      if (File.Exists(_configPath))
        BotHelper.LogDebugToConsole($"[GUILDBOT] Full Path: {Path.GetFullPath(_configPath)}");
      using (var stream = File.Open(_configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        _config = XDocument.Load(stream);

      if (_config.Root == null || _config.Root.Name != "guildbotconfig")
        throw new ApplicationException("Configuration file not found.");

      XElement prefixEl = _config.Root.Element("coreprefix");
      if (prefixEl == null || String.IsNullOrEmpty(prefixEl.Value))
        throw new ApplicationException("Configuration file is missing prefix data.");

      _prefix = prefixEl.Value;
    }

    private void LoadModules()
    {
      var modulesEl = _config.Root.Element("modules");
      if (modulesEl != null)
      {
        if (modulesEl.Element("usercite") != null)
        {
          CitationModule citationModule = new CitationModule(modulesEl, _adminIds, _clientId, _guildPath);
          _modulesDict.Add(citationModule.StringID, citationModule);
          citationModule.GuildBotConfigChanged += CitationModule_ConfigChanged;
        }
        if (modulesEl.Element("userimg") != null)
        {
          ImageModule imageModule = new ImageModule(modulesEl, _adminIds, _clientId, _guildPath);
          _modulesDict.Add(imageModule.StringID, imageModule);
          imageModule.GuildBotConfigChanged += ImageModule_ConfigChanged;
        }
      }
    }

    private void LoadDefaultCommands()
    {
      _commandsList = new List<IBotCommand>()
        {
          new BotCommand("pingcmd",
                         RuleGenerator.PrefixatedCommand(_prefix, "ping"),
                         PingCommand),
          new BotCommand("ctrlcmd",
                         RuleGenerator.PrefixatedCommand(_prefix, "ctrl") &
                         RuleGenerator.UserByID(_superUserID),
                         CtrlCommand),
          new BotCommand("rollcmd",
                         RuleGenerator.PrefixatedCommand(_prefix, "roll"),
                         RollCommand),
          new BotCommand("yesnocmd",
                         RuleGenerator.PrefixatedCommand(_prefix, "yesno"),
                         BinaryChoiceCommand),
          new BotCommand("magic8ballcmd",
                         RuleGenerator.PrefixatedCommand(_prefix, "8ball"),
                         Magic8BallCommand),
          new BotCommand("emojistatscmd",
                         RuleGenerator.PrefixatedCommand(_prefix, "emojistats") &
                         RuleGenerator.RoleByID(455287765995618304u), // VV
                         EmojiStatsCmd)
        };
    }

    private async Task EmojiStatsCmd(SocketMessage msg)
    {
      string logPrefix = "[GUILDBOT][EMOJISTATS]";
      try
      {
        var sw = new System.Diagnostics.Stopwatch();
        sw.Start();

        var emotes = _guild.Emotes;
        var emojiStrings = emotes.Select(e => $"{e}");
        var channels = _guild.TextChannels;

        var timestamp = DateTime.UtcNow;
        var timeDiff = TimeSpan.FromDays(60.0d);
        var timeBoundary = timestamp - timeDiff;

        var emoteTextDict = new Dictionary<string, int>();
        var emoteReacDict = new Dictionary<IEmote, int>();
        var emoteTextDictLock = new object();
        var emoteReacDictLock = new object();

        foreach (var e in emojiStrings)
          emoteTextDict.TryAdd(e, 0);
        foreach (var e in emotes)
          emoteReacDict.TryAdd(e, 0);

        foreach (var textChannel in channels)
        {
          IMessage lastSeenMsg = null;

          int q = 0;

          BotHelper.LogDebugToConsole($"{logPrefix} Started processing of #{textChannel.Name}.");

          var asyncMessages = textChannel.GetMessagesAsync();
          var msgList = new List<IMessage>();
          try
          {
            await foreach (var messages in asyncMessages)
            {
              foreach (var message in messages)
              {
                lastSeenMsg = message;
                if (message.Timestamp < timeBoundary)
                  break;
                msgList.Add(message);
              }
              if (lastSeenMsg.Timestamp < timeBoundary)
                break;
            }
          }
          catch (Exception ex)
          {
            BotHelper.LogExceptionToConsole($"{logPrefix} Fetch Messages failed : #{textChannel.Name}", ex);
            continue;
          }

          while (lastSeenMsg.Timestamp > timeBoundary)
          {
            int listCount;
            try
            {
              listCount = msgList.Count;
              await foreach (var messages in textChannel.GetMessagesAsync(lastSeenMsg, Direction.Before))
              {
                foreach (var message in messages)
                {
                  lastSeenMsg = message;
                  if (message.Timestamp < timeBoundary)
                    break;
                  msgList.Add(message);
                }
                if (lastSeenMsg.Timestamp < timeBoundary)
                  break;
              }
            }
            catch (Exception ex)
            {
              BotHelper.LogExceptionToConsole($"{logPrefix} Fetch Messages failed : #{textChannel.Name}", ex);
              continue;
            }

            BotHelper.LogDebugToConsole($"{logPrefix} Downloaded {msgList.Count} messages from #{textChannel.Name}.");

            if (msgList.Count == listCount)
              break;
          }

          int threads = Environment.ProcessorCount < 3 ? 1 : Environment.ProcessorCount - 1;

          int count = msgList.Count / threads;
          int remainder = msgList.Count % threads;
          List<List<IMessage>> splitList = new List<List<IMessage>>();
          int j = 0;
          for (int i = 0; i < threads; i++)
          {
            int curCount = (i < remainder) ? count + 1 : count;
            splitList.Add(msgList.GetRange(j, curCount));
            j += curCount;
          }

          List<Task> tasklist = new List<Task>();

          foreach (var messagesPerCore in splitList)
            tasklist.Add(Task.Run(() =>
            {
              try
              {
                foreach (var message in messagesPerCore)
                {
                  if (message is IUserMessage um)
                    foreach (var reaction in um.Reactions)
                      if (emoteReacDict.ContainsKey(reaction.Key))
                      {
                        lock (emoteReacDictLock)
                          emoteReacDict[reaction.Key] += reaction.Value.ReactionCount;
                      }

                  foreach (var emote in emoteTextDict.Keys.ToList())
                  {
                    int n = (message.Content.Length - message.Content.Replace(emote, String.Empty, StringComparison.InvariantCulture).Length) / emote.Length;
                    lock (emoteTextDictLock)
                      emoteTextDict[emote] += n;
                  }
                  Interlocked.Increment(ref q);
                }
              }
              catch (Exception ex)
              {
                BotHelper.LogExceptionToConsole($"{logPrefix} Processing Messages failed : #{textChannel.Name}", ex);
              }
            }));

          await Task.WhenAll(tasklist).ConfigureAwait(false);
          BotHelper.LogDebugToConsole($"{logPrefix} Processed messages from #{textChannel.Name}.");
        }

        var emoteTotalDict = new Dictionary<Discord.IEmote, int>(emoteReacDict);
        foreach (var e in emoteTotalDict.Keys.ToList())
          emoteTotalDict[e] += emoteTextDict[$"{e}"];
        var outputs = new List<string>();
        var str = $"**=== Emoji Usage Stats ===**\n\n`{"Emoji",4} \u2502 {"Total",10} \u2502 {"Reactions",10} \u2502 {"Text",10}`\n";

        foreach (var e in emoteTotalDict.OrderByDescending(kvp => kvp.Value))
        {
          str += $"{e.Key,4}`   \u2502 {e.Value,10} \u2502 {emoteReacDict[e.Key],10} \u2502 {emoteTextDict[$"{e.Key}"],10}`{Environment.NewLine}";
          if (str.Length > 1900)
          {
            outputs.Add(str);
            str = String.Empty;
          }
        }
        outputs.Add(str);

        sw.Stop();
        foreach (var output in outputs)
        {
          await msg.Channel.SendMessageAsyncSafe(output).ConfigureAwait(false);
          await Task.Delay(500).ConfigureAwait(false);
        }
        await msg.Channel.SendMessageAsyncSafe($"Total time spent: {sw.Elapsed}").ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        BotHelper.LogExceptionToConsole($"{logPrefix}. Command failed:", ex);
        throw;
      }
    }

    private async Task SaveConfig()
    {
      await Task.Run(() =>
      {
        lock (_configLockObject)
          _config.Save(_configPath);
      }).ConfigureAwait(false);
    }

    private async void ImageModule_ConfigChanged(object sender, ConfigChangedEventArgs eventArgs)
      => await SaveConfig().ConfigureAwait(false);

    private async void CitationModule_ConfigChanged(object sender, ConfigChangedEventArgs eventArgs)
      => await SaveConfig().ConfigureAwait(false);

    private async Task BinaryChoiceCommand(SocketMessage msg)
      => await msg.Channel.SendMessageAsyncSafe($"{(_rng.Next(10) > 4 ? "Так" : "Ні")} {EmojiCodes.Pizdec}").ConfigureAwait(false);

    private async Task Magic8BallCommand(SocketMessage msg)
      => await msg.Channel.SendMessageAsyncSafe(_magic8BallResponses[_rng.Next(_magic8BallResponses.Length)]).ConfigureAwait(false);

    private async Task RollCommand(SocketMessage msg)
    {
      var words = msg.Content.Split(" ");
      int num = 1;
      int size = 6;
      ulong result = 0;

      switch (words.Length)
      {
        case 2:
          Regex diceregex = new Regex(@"^(?<dicenum>\d+)[dD](?<dicesize>\d+)$", RegexOptions.Compiled);
          var dicematch = diceregex.Match(words[1]);

          if (dicematch.Success)
          {
            var diceGroups = dicematch.Groups;

            if (!Int32.TryParse(diceGroups["dicenum"].Value, out num) || !Int32.TryParse(diceGroups["dicesize"].Value, out size))
              return;
          }
          else if (!Int32.TryParse(words[1], out size))
            return;
          break;
        case 3:
          if (!Int32.TryParse(words[1], out num) || !Int32.TryParse(words[2], out size))
            return;
          break;
        default:
          return;
      }

      if (size == 0 || num == 0)
        return;

      for (int i = 0; i < num; i++)
        result += (ulong)_rng.Next(1, size + 1);

      await msg.Channel.SendMessageAsyncSafe($"Ви заролили: {result} {EmojiCodes.Pizdec}").ConfigureAwait(false);
    }

    private async Task PingCommand(SocketMessage msg)
      => await msg.Channel.SendMessageAsyncSafe("Pong!").ConfigureAwait(false);

    private async Task CtrlCommand(SocketMessage msg)
    {
      string msgContent = msg.Content;
      string[] words = msgContent.Split(" ");

      if (words.Length < 2)
        return;

      switch (words[1])
      {
        case "off":
          await SaveConfig().ConfigureAwait(false);
          await msg.Channel.SendMessageAsyncSafe($"Лягаю спати {EmojiCodes.Pepe}").ConfigureAwait(false);
          break;
        case "modules":
          await ModulesCommand(words, msg).ConfigureAwait(false);
          break;
      }
    }

    private async Task ModulesCommand(string[] words, SocketMessage msg)
    {
      if (words.Length < 3)
        return;

      switch (words[2])
      {
        case "list":
          if (_modulesDict.Count == 0)
          {
            await msg.Channel.SendMessageAsyncSafe("Не підключено жодного модуля.").ConfigureAwait(false);
            return;
          }
          string output = $"Список підключених модулів :{Environment.NewLine}```";
          foreach (var kvp in _modulesDict)
            output += $"{kvp.Key} {(kvp.Value.IsActive ? "включений" : "виключений")}{Environment.NewLine}";
          output += "```";

          await msg.Channel.SendMessageAsyncSafe(output).ConfigureAwait(false);
          break;
        case "on":
          if (words.Length < 4)
            return;
          await ChangeModulesState(words, true, msg).ConfigureAwait(false);
          break;
        case "off":
          if (words.Length < 4)
            return;
          await ChangeModulesState(words, false, msg).ConfigureAwait(false);
          break;
      }
    }

    private async Task ChangeModulesState(string[] words, bool newState, SocketMessage msg)
    {
      List<string> modulesToProcess = new List<string>();

      for (int i = 3; i < words.Length; i++)
        modulesToProcess.Add(words[i]);

      IEnumerable<XElement> modulesElList = _config.Root.Element("modules").Elements();

      List<string> modulesProcessed = new List<string>();

      foreach (var moduleStringID in _modulesDict.Keys)
        if (modulesToProcess.Contains(moduleStringID))
          foreach (var moduleEl in modulesElList)
            if (_modulesDict[moduleStringID].ModuleXmlName.ExactAs(moduleEl.Name.ToString()))
              if (Boolean.TryParse(moduleEl.Attribute("on").Value, out bool state))
              {
                if (state == newState)
                  continue;
                moduleEl.Attribute("on").Value = $"{newState}";
                modulesProcessed.Add(moduleStringID);
              }

      if (modulesProcessed.Count == 0)
      {
        await msg.Channel.SendMessageAsyncSafe($"Нічого не було змінено {EmojiCodes.Thonk}").ConfigureAwait(false);
        return;
      }

      string modulesString = String.Empty;

      foreach (var module in modulesProcessed)
      {
        _modulesDict[module].Reconfigure(_config.Root.Element("modules"));
        modulesString += $"{module} ";
      }

      await SaveConfig().ConfigureAwait(false);

      string output = $"Наступні модулі було {(newState ? @"уві" : @"ви")}мкнено : {modulesString} {EmojiCodes.Picardia}";
      await msg.Channel.SendMessageAsyncSafe(output).ConfigureAwait(false);
    }
  }
}
