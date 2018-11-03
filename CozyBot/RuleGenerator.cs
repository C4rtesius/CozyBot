using System;
using System.Collections.Generic;
using System.Text;

using Discord;
using Discord.WebSocket;

namespace DiscordBot1
{
    public static class RuleGenerator
    {
        private static Rule _hasImage = null;

        private static Predicate<Attachment> _isImage = null;

        static RuleGenerator()
        {
            _hasImage = new Rule(
                (msg) =>
                {
                    var atts = msg.Attachments;
                    if (atts.Count > 0)
                    {
                        foreach (var att in atts)
                        {
                            if (_isImage(att))
                            {
                                return true;
                            }
                        }
                    }
                    return false;
                }
            );

            _isImage =
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

        public static Rule HasImage
        {
            get
            {
                return _hasImage;
            }
        }

        public static Predicate<Attachment> IsImage
        {
            get
            {
                return _isImage;
            }
        }

        public static Rule TextIdentity(string text)
        {
            return new Rule(
                (msg) =>
                {
                    return text == msg.Content;
                }
            );
        }

        public static Rule PrefixatedCommand(string prefix, string cmdName)
        {
            return new Rule(
                (msg) =>
                {
                    string text = msg.Content;
                    if (text.StartsWith(prefix))
                    {
                        string deprefixed = text.Remove(0, prefix.Length);
                        string[] words = deprefixed.Split(" ");
                        if (String.Compare(cmdName, words[0]) == 0)
                        {
                            return true;
                        }
                    }
                    return false;
                }
            );
        }

        public static Rule TextTriggerSingle(string trigger)
        {
            return new Rule(
                (msg) =>
                {
                    return msg.Content.Contains(trigger);
                }
            );
        }

        public static Rule TextTriggerList(List<string> triggerList)
        {
            return new Rule(
                (msg) =>
                {
                    foreach (var trigger in triggerList)
                    {
                        if (msg.Content.Contains(trigger))
                        {
                            return true;
                        }
                    }
                    return false;
                }
            );
        }

        public static Rule UserByID(ulong id)
        {
            return new Rule(
                (msg) =>
                {
                    return id == msg.Author.Id;
                }
            );
        }

        public static Rule RoleByID(ulong id)
        {
            return new Rule(
                (msg) =>
                {
                    var user = msg.Author as SocketGuildUser;
                    if (user == null)
                    {
                        return false;
                    }
                    foreach (var role in user.Roles)
                    {
                        if (role.Id == id)
                        {
                            return true;
                        }
                    }
                    return false;
                }
            );
        }

        public static Rule HasRoleByIds(List<ulong> roleIds)
        {
            Rule resultRule = Rule.FalseRule;

            foreach (var roleId in roleIds)
            {
                resultRule = resultRule | RoleByID(roleId);
            }

            return resultRule;
        }
    }

}
