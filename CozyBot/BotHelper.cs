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
        LogExceptionToConsole($"[WARNING] Message send failed in channel: {channel.Name}.", ex);
      }
    }

    public static string BuildExceptionMessage(string message, Exception ex)
    {
      return String.Join(Environment.NewLine,
                         $"[EXCEPT]{message}",
                         $"Exception caught: {ex.Message}",
                         $"Stack trace: {ex.StackTrace}");
    }

    public static void LogExceptionToConsole(string message, Exception ex)
    {
      Console.WriteLine(BuildExceptionMessage(message, ex));
    }
  }
}
