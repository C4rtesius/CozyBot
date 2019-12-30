using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Discord;
using Discord.WebSocket;

namespace CozyBot
{
    public static class RuleGenerator
    {
        public static Rule HasImage { get; private set; }
        public static Predicate<Attachment> IsImage { get; private set; }

        static RuleGenerator()
        {
            HasImage = new Rule(
                (msg) =>
                {
                    var atts = msg.Attachments;
                    if (atts.Count > 0)
                        foreach (var att in atts)
                            if (IsImage(att))
                                return true;

                    return false;
                }
            );

            IsImage =
                (att) =>
                {
                    string filename = att.Filename.ToLower();

                    if ((filename.EndsWith(".png") ||
                        filename.EndsWith(".jpeg") ||
                        filename.EndsWith(".jpg") ||
                        filename.EndsWith(".bmp") ||
                        filename.EndsWith(".gif")) &&
                        (att.Size < 8000000)
                        )
                    {
                        return true;
                    }
                    return false;
                };
        }

        public static Rule TextIdentity(string text)
            => new Rule((msg) => text == msg.Content);

        public static Rule PrefixatedCommand(string prefix, string cmdName)
            //=> new Rule((msg) => ((Func<string, string, bool>)((str, prfx) => (str.CompareTo(prfx) == 0) || str.StartsWith(prfx)))(msg.Content.Trim(), $"{prefix}{cmdName}"));
            => new Rule(
                (msg) =>
                {
                    // TODO : profile different variants and find optimal

                    if (msg.Content.Trim().Equals($"{prefix}{cmdName}") || msg.Content.StartsWith($"{prefix}{cmdName} "))
                        return true;
                    //msg.Content.Split(" ")[0].CompareTo($"{prefix}{cmdName}") == 0;
                    return false;
                }
            );
        //string text = msg.Content;
        //if (text.StartsWith(prefix))
        //{
        //    string deprefixed = text.Remove(0, prefix.Length);
        //    string[] words = deprefixed.Split(" ");
        //    if (String.Compare(cmdName, words[0]) == 0)
        //    {
        //        return true;
        //    }
        //}
        //return false;

        public static Rule TextTriggerSingle(string trigger)
            => new Rule((msg) => msg.Content.Contains(trigger));

        public static Rule TextTriggerList(List<string> triggerList)
            => new Rule(
                (msg) =>
                {
                    foreach (var trigger in triggerList)
                        if (msg.Content.Contains(trigger))
                            return true;
                    
                    return false;
                }
            );

        public static Rule UserByID(ulong id)
            => new Rule((msg) => id == msg.Author.Id);

        public static Rule RoleByID(ulong id)
        {
            return new Rule(
                (msg) =>
                {
                    if (!(msg.Author is SocketGuildUser user))
                        return false;
                    
                    foreach (var role in user.Roles)
                        if (role.Id == id)
                            return true;
                    
                    return false;
                }
            );
        }

        public static Rule HasRoleByIds(List<ulong> roleIds)
        {
            Rule resultRule = Rule.FalseRule;

            foreach (var roleId in roleIds)
                resultRule |= RoleByID(roleId);

            return resultRule;
        }
    }

}
