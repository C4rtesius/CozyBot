using Discord.WebSocket;

namespace CozyBot
{
  interface IGuildModule : IBotModule
  {
    SocketGuild Guild { get; }
  }
}
