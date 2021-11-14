using System;
using System.Threading.Tasks;

using Discord.WebSocket;

namespace CozyBot
{
  static class BotHelper
  {
    private static object _consoleLock = new object();

    public static async Task SendMessageAsyncSafe(this ISocketMessageChannel channel, string content)
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

    public static async Task DeleteAsyncSafe(this SocketMessage msg, string prefixData = default(string))
    {
      try
      {
        if (msg != null)
          await msg.DeleteAsync().ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        LogExceptionToConsole($"[WARNING]{prefixData ?? String.Empty} Message deletion failed in {msg.Channel.Name}.", ex);
      }
    }

    public static string BuildExceptionMessage(string message, Exception ex)
      => String.Join(Environment.NewLine,
                     $"[EXCEPT]{message}",
                     $"Exception caught: {ex.Message}",
                     $"Stack trace: {ex.StackTrace}");

    public static void LogExceptionToConsole(string message, Exception ex)
      => WriteToConsole(BuildExceptionMessage(message, ex));

    public static void LogDebugToConsole(string message)
    {
#if DEBUG
      WriteToConsole($"[DEBUG]{message}");
#endif
    }

    public static void WriteToConsole(string message)
    {
      lock(_consoleLock)
        Console.WriteLine(message);
    }

    public static bool ExactAs(this string caller, string other)
    {
      if (String.IsNullOrEmpty(caller) || String.IsNullOrEmpty(other))
        return false;
      return String.Compare(caller, other, StringComparison.InvariantCulture) == 0;
    }
  }
}
