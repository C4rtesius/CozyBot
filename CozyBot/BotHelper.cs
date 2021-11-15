using System;
using System.Threading.Tasks;

using Discord;
using Discord.Rest;
using Discord.WebSocket;

namespace CozyBot
{
  static class BotHelper
  {
    private static object _consoleLock = new object();

    public static async Task<RestUserMessage> SendMessageAsyncSafe(this ISocketMessageChannel channel, string content)
    {
      try
      {
        if (channel != null && !String.IsNullOrEmpty(content))
          return await channel.SendMessageAsync(content).ConfigureAwait(false);
        return null;
      }
      catch (Exception ex)
      {
        ex.LogToConsole($"[WARNING] Message send failed in channel: {channel.Name}.");
        return null;
      }
    }

    public static async Task<IUserMessage> SendMessageAsyncSafe(this IDMChannel channel, string content)
    {
      try
      {
        if (channel != null && !String.IsNullOrEmpty(content))
          return await channel.SendMessageAsync(content).ConfigureAwait(false);
        return null;
      }
      catch (Exception ex)
      {
        ex.LogToConsole($"[WARNING] Message send failed in channel: {channel.Recipient.Username}#{channel.Recipient.Discriminator}.");
        return null;
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
        ex.LogToConsole($"[WARNING]{prefixData ?? String.Empty} Message deletion failed in {msg.Channel.Name}.");
      }
    }

    public static string BuildExceptionMessage(string message, Exception ex)
      => String.Join(Environment.NewLine,
                     $"[EXCEPT]{message}",
                     $"Exception caught: {ex.Message}",
                     $"Stack trace: {ex.StackTrace}");

    public static void LogToConsole(this Exception ex, string message)
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
