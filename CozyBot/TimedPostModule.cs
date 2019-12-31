using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace DiscordBot1
{
    class TimedPostModule : IBotModule
    {
        private bool _isActive = false;
        private static string _stringId = "TimedPicturesModule";
        private static string _moduleXmlName = "timedpic";

        public event ConfigChanged ConfigChanged;

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
                return _stringId;
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

        public TimedPostModule(XElement configEl, ulong ownerId)
        {
            throw new NotImplementedException();
        }

        public void Reconfigure(XElement configEl)
        {
            throw new NotImplementedException();
        }
    }
}
