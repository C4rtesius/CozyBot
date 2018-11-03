using System;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

using Discord;
using Discord.WebSocket;

namespace DiscordBot1
{
    public interface IBotCommand
    {
        Guid ID { get; }
        bool CanExecute(SocketMessage msg);
        Task ExecuteCommand(SocketMessage msg);
    }
}
