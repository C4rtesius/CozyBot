using System;
using System.Threading.Tasks;

using Discord.WebSocket;

namespace CozyBot
{
  static class BotHelper
  {
    public static async Task SendMessageAsyncSafe(ISocketMessageChannel channel, string content)
    {
      try
      {
        if (channel != null && !String.IsNullOrEmpty(content))
          await channel.SendMessageAsync(content).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        Console.WriteLine(String.Join(Environment.NewLine,
                                      $"[WARNING][EXCEPT] Message send failed in channel: {channel.Name}.",
                                      $"Exception caught: {ex.Message}",
                                      $"Stack trace: {ex.StackTrace}"));
      }
    }
  }
}
