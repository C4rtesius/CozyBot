using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace DiscordBot1
{
    public class ModerationModule : IBotModule
    {
        private bool _isActive = false;

        private static string _stringID = "ModerationModule";
        private static string _moduleXmlName = "mod";

        public bool IsActive
        {
            get
            {
                return _isActive;
            }
        }

        public string StringID
        {
            get
            {
                return _stringID;
            }
        }

        public string ModuleXmlName 
        {
            get
            {
                return _moduleXmlName;
            }
        }

        public IEnumerable<IBotCommand> ActiveCommands => throw new NotImplementedException();

        protected event ConfigChanged _configChanged;

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
    }
}
