using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace DiscordBot1
{
    public interface IBotModule
    {
        bool IsActive { get; }
        string StringID { get; }
        string ModuleXmlName { get; }
        IEnumerable<IBotCommand> ActiveCommands { get; }

        event ConfigChanged GuildBotConfigChanged;
        void Reconfigure(XElement configEl);
    }
}
