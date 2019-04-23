using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;
using Discord;
using Discord.WebSocket;

namespace CozyBot
{
    /// <summary>
    /// Module provides complex voting parliament-like system.
    /// </summary>
    class ParliamentModule : IGuildModule
    {
        public SocketGuild Guild => throw new NotImplementedException();

        public bool IsActive => throw new NotImplementedException();

        public string StringID => throw new NotImplementedException();

        public string ModuleXmlName => throw new NotImplementedException();

        public IEnumerable<IBotCommand> ActiveCommands => throw new NotImplementedException();

        public event ConfigChanged ConfigChanged;

        public void Reconfigure(XElement configEl)
        {
            throw new NotImplementedException();
        }
    }
}
