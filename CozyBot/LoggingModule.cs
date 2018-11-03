using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace DiscordBot1
{
    public class LoggingModule : IBotModule
    {
        private bool _isActive = false;
        private static string _stringID = "LoggingModule";
        private static string _moduleXmlName = "log";

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

        private Queue<string> _logQueue;

        private IBotCommand _cfgCommand;
        private IBotCommand _dumpLogCommand;

        public IEnumerable<IBotCommand> ActiveCommands
        {
            get
            {
                yield return _cfgCommand;
                yield return _dumpLogCommand;
            }
        }

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

        public LoggingModule(XElement configEl)
        {

        }

        public void Reconfigure(XElement configEl)
        {
            throw new NotImplementedException();
        }
    }
}
