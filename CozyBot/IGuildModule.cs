using System;
using System.Collections.Generic;
using System.Text;

using Discord.WebSocket;

namespace CozyBot
{
    interface IGuildModule : IBotModule
    {
        SocketGuild Guild { get; }
    }
}
