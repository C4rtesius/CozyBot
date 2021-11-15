using System;
using System.Threading.Tasks;
using System.Collections.Generic;

using Discord;
using Discord.WebSocket;

namespace CozyBot
{
  static class BotHelper
  {
    private static readonly object _consoleLock = new object();
    private const int _msgSizeLimit = 1800;

    public static async Task<IUserMessage> SendMessageAsyncSafe(this IMessageChannel channel, string content)
    {
      try
      {
        if (channel != null && !String.IsNullOrEmpty(content))
          return await channel.SendMessageAsync(content).ConfigureAwait(false);
        return null;
      }
      catch (Exception ex)
      {
        string suffix = (channel is IDMChannel dm) ? $"{dm.Recipient.Username}#{dm.Recipient.Discriminator}" : $"{channel.Name}";
        ex.LogToConsole($"[WARNING] Message send failed in channel: {suffix}.");
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

    private static string BuildExceptionMessage(string message, Exception ex)
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

    private static IEnumerable<string> GenerateOutputMessages<T>(string input,
                                                                 Func<T, string> transform,
                                                                 IEnumerable<T> source,
                                                                 Func<string, string> openMsg,
                                                                 Func<string, string> closeMsg)
    {
      string current = input;
      foreach (var element in source)
      {
        string temp = transform(element);
        if (current.Length + temp.Length < _msgSizeLimit)
          current = $"{current}{temp}";
        else
        {
          current = closeMsg(current);
          yield return current;
          current = openMsg(temp);
        }
      }
      current = closeMsg(current);
      yield return current;
    }

    public static async Task GenerateAndSendOutputMessages<T>(this IMessageChannel channel,
                                                              string input,
                                                              IEnumerable<T> source,
                                                              Func<T, string> transform,
                                                              Func<string, string> openMsg,
                                                              Func<string, string> closeMsg)
    {
      foreach (var message in GenerateOutputMessages(input, transform, source, openMsg, closeMsg))
        await channel.SendMessageAsyncSafe(message).ConfigureAwait(false);
    }

    private static async IAsyncEnumerable<string> GenerateOutputMessages<T>(string input,
                                                                            Func<T, Task<string>> transform,
                                                                            IEnumerable<T> source,
                                                                            Func<string, string> openMsg,
                                                                            Func<string, string> closeMsg)
    {
      string current = input;
      foreach (var element in source)
      {
        string temp = await transform(element).ConfigureAwait(false);
        if (current.Length + temp.Length < _msgSizeLimit)
          current = $"{current}{temp}";
        else
        {
          yield return closeMsg(current);
          current = openMsg(temp);
        }
      }
      yield return closeMsg(current);
    }

    public static async Task GenerateAndSendOutputMessages<T>(this IMessageChannel channel,
                                                              string input,
                                                              IEnumerable<T> source,
                                                              Func<T, Task<string>> transform,
                                                              Func<string, string> openMsg,
                                                              Func<string, string> closeMsg)
    {
      await foreach (var message in GenerateOutputMessages(input, transform, source, openMsg, closeMsg))
        await channel.SendMessageAsyncSafe(message).ConfigureAwait(false);
    }
  }
}
