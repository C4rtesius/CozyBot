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
        private static string _stringID = "ParliamentModule";
        private static string _moduleXmlName = "parliament";
        private static string _moduleFolder = @"parliament\";
        private static string _configFileName = "ParliamentModuleConfig.xml";

        protected List<IBotCommand> _cfgCommands = new List<IBotCommand>();
        protected List<IBotCommand> _useCommands = new List<IBotCommand>();

        private SocketGuild _guild;
        protected bool _isActive;
        protected event ConfigChanged _configChanged;

        public SocketGuild Guild
        {
            get
            {
                return _guild;
            }
        }

        public bool IsActive
        {
            get
            {
                return _isActive;
            }
        }

        public string StringID { get { return _stringID; } }

        public string ModuleXmlName { get { return _moduleXmlName; } }

        public IEnumerable<IBotCommand> ActiveCommands
        {
            get
            {
                foreach (var cmd in _cfgCommands)
                {
                    yield return cmd;
                }
                foreach (var cmd in _useCommands)
                {
                    yield return cmd;
                }
            }
        }

        public event ConfigChanged ConfigChanged
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

        public void Reconfigure(XElement configEl)
        {
            throw new NotImplementedException();
        }

        public ParliamentModule()
        {

        }
    }
}
