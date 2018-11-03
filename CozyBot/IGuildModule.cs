using System;
using System.Collections.Generic;
using System.Text;

using Discord.WebSocket;

namespace DiscordBot1
{
    interface IGuildModule : IBotModule
    {
        SocketGuild Guild { get; }
    }
}
