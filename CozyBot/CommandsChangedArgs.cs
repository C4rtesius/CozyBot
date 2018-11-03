using System;
using System.Collections.Generic;
using System.Text;

namespace DiscordBot1
{
    public class CommandsChangedArgs : EventArgs
    {
        //Private Fields
        private IEnumerable<IBotCommand> _commands;

        //Public Properties
        public IEnumerable<IBotCommand> NewCommands
        {
            get
            {
                return _commands;
            }
        }

        public CommandsChangedArgs(IEnumerable<IBotCommand> commands)
            : base()
        {
            _commands = commands;
        }
    }
}
