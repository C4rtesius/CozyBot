using System;
using System.Text;
using System.Xml.Linq;
using System.Collections.Generic;

using Discord;
using Discord.WebSocket;

namespace CozyBot
{
    public class PxlsAlertsModule : IGuildModule
    {
        private static string _stringID = "PxlsAlertsModule";
        private static string _moduleXmlName = "pxls-alerts";
        private SocketGuild _guild;

        protected bool _isActive;
        protected List<IBotCommand> _useCommands = new List<IBotCommand>();
        protected event ConfigChanged _configChanged;


        public string StringID => _stringID;
        public SocketGuild Guild => _guild;
        public bool IsActive => _isActive;
        public string ModuleXmlName => _moduleXmlName;

        public event ConfigChanged GuildBotConfigChanged
        {
            add
            {
                if (value != null)
                {
                    _configChanged += value;
                }
            }
            remove
            {
                if (value != null)
                {
                    _configChanged -= value;
                }
            }
        }

        public IEnumerable<IBotCommand> ActiveCommands
        {
            get
            {
                foreach (var cmd in _useCommands)
                    yield return cmd;
            }
        }

        public void Reconfigure(XElement el)
        { }
    }
}
