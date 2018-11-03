using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace DiscordBot1
{
    public class ConfigChangedArgs : EventArgs
    {
        private XElement _newConfigEl;

        public XElement NewConfigElement
        {
            get
            {
                return _newConfigEl;
            }
        }

        public ConfigChangedArgs(XElement newConfigEl)
            : base()
        {
            if (newConfigEl == null)
            {
                throw new ArgumentNullException("New Configuration Args cannot be null.");
            }

            _newConfigEl = newConfigEl;
        }
    }
}
