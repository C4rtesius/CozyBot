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

    protected override string ListItemString => "пікч";
    protected override string DeletedItemString => "пікчі";

    /// <summary>
    /// Module working folder.
    /// </summary>
    protected override string ModuleFolder => "userimg";

    /// <summary>
    /// String for citation usage count in XML.
    /// </summary>
    private const string _usageAttrName = "used";

    /// <summary>
    /// ConcurrentDictionary to implement ratelimiting per user, per channel, per command key.
    /// </summary>
    private ConcurrentDictionary<string, Task> _ratelimitDict;

    private IMessageChannel _imgServiceChannel;

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
      "search",
      "view"
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
    /// <param name="imgServiceChannel">Service channel for image storage.</param>
    public ImageModule(XElement configEl, List<ulong> adminIds, ulong clientId, string workingPath, IMessageChannel imgServiceChannel)
      : base(configEl, adminIds, clientId, workingPath)
    {
      if (!Directory.Exists(Path.Combine(_guildPath, ModuleFolder)))
        Directory.CreateDirectory(Path.Combine(_guildPath, ModuleFolder));
      _imgServiceChannel = Guard.NonNull(imgServiceChannel, nameof(imgServiceChannel));

      _ratelimitDict = new ConcurrentDictionary<string, Task>();
    }

    protected override void GenerateUseCommands(List<ulong> perms)
    {
      // Generate base use commands from ContentModule
      base.GenerateUseCommands(perms);

      // Add verbose listing command
      List<ulong> allListPerms = new List<ulong>(_adminIds);
      allListPerms.AddRange(perms);
      Rule viewRule = RuleGenerator.HasRoleByIds(allListPerms) & RuleGenerator.PrefixatedCommand(_prefix, "view");

      _useCommands.Add(new BotCommand($"{StringID}-viewcmd", viewRule, ViewCommand));
    }

    protected override Func<SocketMessage, Task> UseCommandGenerator(string key)
    {
      return async (msg) =>
      {
        await msg.DeleteAsyncSafe($"[{LogPref}][USE]").ConfigureAwait(false);

        string dictKey = $"{msg.Author.Id}{msg.Channel.Id}{key}";
        if (_ratelimitDict.ContainsKey(dictKey))
          return;
        _ratelimitDict.TryAdd(dictKey, Task.Run(async() =>
          { await Task.Delay(10000).ConfigureAwait(false); _ratelimitDict.TryRemove(dictKey, out _); }));

        var imgList = GetItemsListByKey(key);
        if (imgList.Count == 0)
          return;

        XElement imgEl = imgList[_rnd.Next() % imgList.Count];
        string imgFileName = imgEl.Value;

        try
        {
          await SendFileTask(msg.Channel, Path.Combine(_guildPath, ModuleFolder, imgFileName)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          ex.LogToConsole($"[{LogPref}] File send failed: {key}");
          throw;
        }

        var usageCount = imgEl.GetOrCreateDefaultAttributeValue(_usageAttrName, 0);
        imgEl.Attribute(_usageAttrName).Value = $"{++usageCount}";

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
    public async Task<IUserMessage> SendFileTask(IMessageChannel channel, string filePath)
      => await channel.SendFileAsync(filePath).ConfigureAwait(false);

    protected async Task DownloadFile(Attachment att, ISocketMessageChannel sc, string filepath)
    {
      if (File.Exists(filepath))
      {
        await sc.SendMessageAsyncSafe($"Пікча з такою назвою вже є {EmojiCodes.Tomas}").ConfigureAwait(false);
        return;
      }

      using HttpClient hc = new HttpClient();
      using FileStream fs = new FileStream(filepath, FileMode.Create, FileAccess.Write);
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

        string filepath = Path.Combine(_guildPath, ModuleFolder, newItemFileName);

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

    protected override Embed BuildHelpEmbed(SocketGuildUser user)
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

    protected virtual async Task ViewCommand(SocketMessage msg)
    {
      string cmdPrefix = $"[{LogPref}][VIEW]";
      await msg.DeleteAsyncSafe(cmdPrefix).ConfigureAwait(false);

      if (!(msg.Author is SocketGuildUser user))
        return;
      var regexMatch = Regex.Match(msg.Content, ListCommandRegex);
      if (!regexMatch.Success)
        return;

      var cmdKey = regexMatch.Groups["key"].Value;
      var itemsDict = new Dictionary<string, XElement>();
      RPItemDictGenerator(GetRootByKey(cmdKey), String.IsNullOrWhiteSpace(cmdKey) ? String.Empty : $"{cmdKey}.", itemsDict);
      if (!itemsDict.Any())
        return;

      var itemsList = itemsDict.ToList();
      int index = 0;
      int oldIndex = -1;
      var embedMessage = await msg.Channel.SendMessageAsyncSafe("*Чекайте...*").ConfigureAwait(false);
      Emoji leftArrowEmoji = new Emoji("\u2b05\ufe0f");
      Emoji rightArrowEmoji = new Emoji("\u27a1\ufe0f");
      await embedMessage.AddReactionAsync(leftArrowEmoji).ConfigureAwait(false);
      await embedMessage.AddReactionAsync(rightArrowEmoji).ConfigureAwait(false);
      using ManualResetEventSlim mres = new ManualResetEventSlim(false);

      Task waiter = Task.Run(async () =>
      {
        int timeoutTries = 12;
        while (timeoutTries-- > 0)
        {
          var reactLeftUsers = await embedMessage.GetReactionUsersAsync(leftArrowEmoji, 50).FlattenAsync().ConfigureAwait(false);
          var reactRightUsers = await embedMessage.GetReactionUsersAsync(rightArrowEmoji, 50).FlattenAsync().ConfigureAwait(false);
          bool isLeft = reactLeftUsers.Any(u => u.Id == msg.Author.Id);
          bool isRight = reactRightUsers.Any(u => u.Id == msg.Author.Id);
          if (isRight || isLeft)
            timeoutTries = 12;
          if (isRight ^ isLeft)
          {
            if (isLeft)
            {
              index = --index == -1 ? itemsList.Count - 1 : index;
              await embedMessage.RemoveReactionAsync(leftArrowEmoji, msg.Author).ConfigureAwait(false);
            }
            if (isRight)
            {
              index = ++index == itemsList.Count ? 0 : index;
              await embedMessage.RemoveReactionAsync(rightArrowEmoji, msg.Author).ConfigureAwait(false);
            }
          }
          await Task.Delay(5000).ConfigureAwait(false);
        }
        if (mres != null)
          mres.Set();
      });

      string headerStr = String.Empty;
      string descStr = String.Empty;
      string picUrl = String.Empty;

      while (mres != null && !mres.IsSet)
      {
        if (index != oldIndex)
        {
          oldIndex = index;
          var picKVP = itemsList[index];
          picUrl = picKVP.Value.GetOrCreateDefaultAttributeValue("url", String.Empty);
          if (String.IsNullOrEmpty(picUrl))
          {
            var fileMsg = await SendFileTask(_imgServiceChannel, Path.Combine(_guildPath, ModuleFolder, picKVP.Value.Value)).ConfigureAwait(false);
            picUrl = (fileMsg.Attachments.FirstOrDefault() ??
                      throw new ApplicationException("Failed to upload image.")).Url;
            picKVP.Value.Attribute("url").Value = picUrl;
            try
            {
              await ModuleConfigChanged().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
              ex.LogToConsole($"{cmdPrefix} Config save failed.");
            }
          }
          headerStr = $"{(String.IsNullOrEmpty(cmdKey) ? String.Empty : $"за ключем `{cmdKey}` ")}({index + 1}/{itemsList.Count})";
          descStr = $"`{picKVP.Key}`";
          await embedMessage.ModifyAsync(properties =>
          {
            properties.Embed = BuildViewEmbed(user, headerStr, descStr, picUrl);
            properties.Content = String.Empty;
          }).ConfigureAwait(false);
        }
        await Task.Delay(5000).ConfigureAwait(false);
      }

      await embedMessage.ModifyAsync(properties =>
      {
        properties.Embed = BuildViewEmbed(user, $"{headerStr} **Закінчено.**", descStr, picUrl);
        properties.Content = String.Empty;
      }).ConfigureAwait(false);
    }

    protected virtual Embed BuildViewEmbed(SocketGuildUser user, string header, string description, string url)
    {
      var guild = user.Guild;

      var eba = new EmbedAuthorBuilder
      {
        Name = guild.GetUser(_clientId).Nickname,
        IconUrl = guild.GetUser(_clientId).GetAvatarUrl()
      };

      var efob = new EmbedFooterBuilder
      {
        Text = "Оффнуть картинки - еще не самое проблемное."
      };

      var eb = new EmbedBuilder
      {
        Author = eba,
        Color = Color.Green,
        Title = $"Перегляд зображень {header}",
        Description = description,
        ImageUrl = url,
        Timestamp = DateTime.Now,
        Footer = efob
      };
      return eb.Build();
    }

    public static async Task<IMessageChannel> GetOrCreateServiceChannel(SocketGuild guild, SocketGuild serviceGuild)
    {
      Guard.NonNull(guild, nameof(guild));
      Guard.NonNull(serviceGuild, nameof(serviceGuild));
      string channelName = $"{guild.Id}-img";

      return serviceGuild.TextChannels.FirstOrDefault(ch => ch.Name.ExactAs(channelName)) as IMessageChannel ??
             await serviceGuild.CreateTextChannelAsync(channelName).ConfigureAwait(false);
    }
  }
}
