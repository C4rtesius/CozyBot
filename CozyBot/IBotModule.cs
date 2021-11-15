using System.Xml.Linq;
using System.Collections.Generic;

namespace CozyBot
{
  public interface IBotModule
  {
    bool IsActive { get; }
    string StringID { get; }
    string LogPref { get; }
    string ModuleXmlName { get; }
    IEnumerable<IBotCommand> ActiveCommands { get; }

    event ConfigChanged GuildBotConfigChanged;
    void Reconfigure(XElement configEl);
  }
}
