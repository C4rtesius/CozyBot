using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Discord;
using Discord.WebSocket;

namespace CozyBot
{
  public sealed class Program : IDisposable
  {
    // Private members section

    private string _prefix;
    private string _token;
    private string _appBaseDirectory;
    private string _configFileName = "config.xml";
    private XDocument _config;
    private DiscordSocketClient _client;
    private ManualResetEvent _mre = new ManualResetEvent(false);
    private Dictionary<ulong, GuildBot> _guildBotsDict;
    private bool _disposedValue;

    private event CtrlEventHandler _ctrlEventHandler;

    // Public methods section

    public static async Task Main(string[] args)
    {
      try
      {
        using var p = new Program();
        await p.MainAsync().ConfigureAwait(false);
      }
      catch
      {
        BotHelper.WriteToConsole("[FATAL][CORE] Shutting down.");
      }
    }

    // Private methods section

    private async Task MainAsync()
    {
      try
      {
        BotHelper.LogDebugToConsole(String.Join(Environment.NewLine,
                                                $"[CORE] Current working dir :{Directory.GetCurrentDirectory()}",
                                                $"Current AppDomain BaseDirectory :{AppDomain.CurrentDomain.BaseDirectory}"));
        _guildBotsDict = new Dictionary<ulong, GuildBot>();

        var discordConfig = new DiscordSocketConfig();
        discordConfig.GatewayIntents = GatewayIntents.All;

        _client = new DiscordSocketClient(discordConfig);
        _client.Log += Log;

        await LoadCoreConfig().ConfigureAwait(false);
        await _client.LoginAsync(TokenType.Bot, _token).ConfigureAwait(false);
        await _client.StartAsync().ConfigureAwait(false);

        _client.Ready += () => Task.Run(ConfigGuilds);

        AppDomain.CurrentDomain.ProcessExit += (o, e) => OnCtrlEvent(CtrlType.CTRL_C_EVENT);

        _client.MessageReceived += MessageReceived;

        _mre.WaitOne();
      }
      catch (Exception ex)
      {
        ex.LogToConsole("[FATAL][CORE] Unrecoverable error occurred.");
        throw;
      }
    }

    private void OnCtrlEvent(CtrlType type)
    {
      BotHelper.LogDebugToConsole("[CORE] ProcessExit triggered.");

      _ctrlEventHandler.Invoke(type);
      _mre.Set();
    }

    private async Task LoadCoreConfig()
    {
      _appBaseDirectory = AppDomain.CurrentDomain.BaseDirectory;
      await LoadConfig().ConfigureAwait(false);
    }

    private async Task ConfigGuilds()
    {
      // move this to the config later
      const ulong serviceGuildId= 910122574527234058UL;

      var guilds = _client.Guilds.Where(g => g.Id != serviceGuildId);
      var serviceGuild = _client.Guilds.First(g => g.Id == serviceGuildId);

      foreach (var guild in guilds)
      {
        ulong guildID = guild.Id;
        string guildPath = Path.Combine(_appBaseDirectory, guildID.ToString());
        string guildConfigPath = Path.Combine(guildPath, "config.xml");
        await Task.Run(() =>
        {
          BotHelper.LogDebugToConsole($"[CORE] guildPath:{guildPath}");

          if (!Directory.Exists(guildPath))
          {
            BotHelper.LogDebugToConsole($"[CORE] Creating guildPath:{guildPath}");

            Directory.CreateDirectory(guildPath);
          }
          if (!File.Exists(guildConfigPath))
          {
            BotHelper.LogDebugToConsole($"[CORE] Creating guild config:{guildConfigPath}");

            GetDefaultGuildConfig().Save(guildConfigPath);
          }
        }).ConfigureAwait(false);

        GuildBot newBot = new GuildBot(guild, serviceGuild, guildPath, _client.CurrentUser.Id);

        _ctrlEventHandler += newBot.OnCtrlEvent;

        _guildBotsDict.TryAdd(guildID, newBot);
      }
    }

    private XDocument GetDefaultGuildConfig()
    {
      XDocument newConfig = new XDocument();
      XElement root = new XElement("guildbotconfig",
                                   new XElement("coreprefix", _prefix),
                                   new XElement("modules",
                                                new XElement("usercite",
                                                new XAttribute("on", $"{true}"),
                                                new XAttribute("prefix", "c!")),
                                   new XElement("userimg",
                                                new XAttribute("on", $"{true}"),
                                                new XAttribute("prefix", "i!"))));
      newConfig.Add(root);

      return newConfig;
    }

    private async Task LoadConfig()
    {
      await Task.Run(() =>
      {
        BotHelper.LogDebugToConsole($"[CORE] Load Config Path :{Path.Combine(_appBaseDirectory, _configFileName)}");

        if (File.Exists(Path.Combine(_appBaseDirectory, _configFileName)))
          using (var stream = File.Open(Path.Combine(_appBaseDirectory, _configFileName), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            _config = XDocument.Load(stream);
        else
          throw new ApplicationException("Configuration file not found!");
      }).ConfigureAwait(false);

      if (_config.Root.Name != "botconfig")
        throw new ApplicationException("Configuration file not found.");

      XElement tokenEl = _config.Root.Element("token");
      if (tokenEl == null || String.IsNullOrEmpty(tokenEl.Value))
        throw new ApplicationException("Configuration file is missing token data.");
      _token = tokenEl.Value;

      XElement prefixEl = _config.Root.Element("coreprefix");
      if (prefixEl == null || String.IsNullOrEmpty(prefixEl.Value))
        throw new ApplicationException("Configuration file is missing prefix data.");
      _prefix = prefixEl.Value;
    }

    private Task Log(LogMessage logMsg)
    {
      BotHelper.WriteToConsole(logMsg.ToString());
      return Task.CompletedTask;
    }

    private async Task MessageReceived(SocketMessage msg)
    {
      if (msg.Channel is SocketGuildChannel scg)
      {
        ulong id = scg.Guild.Id;
        await Task.Run(() =>
        {
          if (_guildBotsDict.TryGetValue(id, out var bot))
            bot.Dispatch(msg);
        }).ConfigureAwait(false);
      }
    }

    private void Dispose(bool disposing)
    {
      if (!_disposedValue)
      {
        if (disposing)
        {
          if (_client != null)
            _client.Dispose();
          if (_mre != null)
            _mre.Dispose();
        }

        _disposedValue = true;
      }
    }

    public void Dispose()
    {
      // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
      Dispose(disposing: true);
      GC.SuppressFinalize(this);
    }
  }
}
