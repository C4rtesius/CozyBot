using System;
using System.Collections.Generic;
using System.Text;

namespace DiscordBot1
{
    interface IBot
    {
        Dictionary<string, IBotModule> ModulesDict { get; }
    }
}
