using System;
using System.Threading.Tasks;

using Discord.WebSocket;

namespace CozyBot
{
  public interface IBotCommand
  {
    Guid ID { get; }
    bool CanExecute(SocketMessage msg);
    Task ExecuteCommand(SocketMessage msg);
  }
}
