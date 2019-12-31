using System;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

using Discord.WebSocket;

namespace CozyBot
{
    public class BotCommand : IBotCommand
    {
        //Private Fields
        private Rule _executeRule;
        private Func<SocketMessage, Task> _cmd;
        private string _stringID;
        private Guid _id;

        static Rule _alwaysCanExecute = Rule.TrueRule;
        static Func<SocketMessage, Task> _emptyExecution = async (msg) => { await Task.CompletedTask; };
        static BotCommand _emptyCommand;

        //Public Properties
        public Guid ID
        {
            get
            {
                return _id;
            }
        }

        //Constructors
        static BotCommand()
        {
            _emptyCommand = new BotCommand("emptycmd", Rule.FalseRule, _emptyExecution);
        }

        public BotCommand(string stringID, Rule executeRule, Func<SocketMessage, Task> cmd)
        {
            _stringID = Guard.NonNullWhitespaceEmpty(stringID, nameof(stringID));

            _executeRule = executeRule ?? _alwaysCanExecute;
            _cmd = cmd ?? _emptyExecution;

            _id = Guid.NewGuid();
        }

        //Public methods

        public bool CanExecute(SocketMessage msg)
        {
            return _executeRule.Check(msg);
        }

        public async Task ExecuteCommand(SocketMessage msg)
        {
            await _cmd(msg).ConfigureAwait(false);
        }


        public static Rule AlwaysExecute
        {
            get
            {
                return _alwaysCanExecute;
            }
        }

        public static Func<SocketMessage, Task> EmptyExecution
        {
            get
            {
                return _emptyExecution;
            }
        }

        public static BotCommand EmptyCommand
        {
            get
            {
                return _emptyCommand;
            }
        }
    }
}
