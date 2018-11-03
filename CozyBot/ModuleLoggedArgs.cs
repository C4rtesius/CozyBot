using System;
using System.Collections.Generic;
using System.Text;

using Discord;

namespace DiscordBot1
{
    public class ModuleLoggedArgs : EventArgs
    {
        private Embed _logMessageEmbed;

        public Embed LogMessageEmbed
        {
            get
            {
                return _logMessageEmbed;
            }
        }

        public ModuleLoggedArgs(Embed logEmbed)
            : base()
        {
            _logMessageEmbed = logEmbed;
        }
    }
}
