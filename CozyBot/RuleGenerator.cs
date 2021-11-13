using System;
using System.Linq;
using System.Collections.Generic;

using Discord;
using Discord.WebSocket;

namespace CozyBot
{
  public static class RuleGenerator
  {
    public static Rule HasImage { get; }
    public static Predicate<Attachment> IsImage { get; }

    static RuleGenerator()
    {
      HasImage = new Rule((msg) =>
      {
        var atts = msg.Attachments;
        if (atts.Count == 0)
          return false;
        foreach (var att in atts)
          if (IsImage(att))
            return true;

        return false;
      });

      IsImage = (att) =>
      {
        string filename = att.Filename.ToLower();

        return (filename.EndsWith(".png")  ||
                filename.EndsWith(".jpeg") ||
                filename.EndsWith(".jpg")  ||
                filename.EndsWith(".bmp")  ||
                filename.EndsWith(".gif")) &&
               (att.Size < 8000000);
      };
    }

    public static Rule TextIdentity(string text)
      => new Rule((msg) => text == msg.Content);

    public static Rule PrefixatedCommand(string prefix, string cmdName)
      => new Rule((msg) => msg.Content.Trim().Equals($"{prefix}{cmdName}") || msg.Content.StartsWith($"{prefix}{cmdName} "));

    public static Rule TextTriggerSingle(string trigger)
      => new Rule((msg) => msg.Content.Contains(trigger));

    public static Rule TextTriggerList(List<string> triggerList)
      => new Rule((msg) => triggerList.Any(trigger => msg.Content.Contains(trigger)));

    public static Rule UserByID(ulong id)
      => new Rule((msg) => id == msg.Author.Id);

    public static Rule RoleByID(ulong id)
      => new Rule((msg) =>
      {
        if (!(msg.Author is SocketGuildUser user))
          return false;

        return user.Roles.Any(role => role.Id == id);
      });

    public static Rule HasRoleByIds(List<ulong> roleIds)
    {
      Rule resultRule = Rule.FalseRule;

      foreach (var roleId in roleIds)
        resultRule |= RoleByID(roleId);

      return resultRule;
    }
  }
}
