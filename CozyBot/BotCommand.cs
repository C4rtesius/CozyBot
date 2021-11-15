using System;
using System.Globalization;
using System.Threading.Tasks;

using Discord.WebSocket;

namespace CozyBot
{
  public class BotCommand : IBotCommand
  {
    public static Rule AlwaysExecute { get; } = Rule.TrueRule;
    public static Func<SocketMessage, Task> EmptyExecution { get; } = async (msg) => { await Task.CompletedTask; };
    public static BotCommand EmptyCommand { get; } = new BotCommand("emptycmd", Rule.FalseRule, EmptyExecution);

    private readonly Rule _executeRule;
    private readonly Func<SocketMessage, Task> _cmd;
    private readonly string _stringID;

    public BotCommand(string stringID, Rule executeRule, Func<SocketMessage, Task> cmd)
    {
      _stringID = Guard.NonNullWhitespaceEmpty(stringID, nameof(stringID));
      _executeRule = executeRule ?? AlwaysExecute;
      _cmd = cmd ?? EmptyExecution;
      ID = Guid.NewGuid();
    }

    public Guid ID { get; }

    public bool CanExecute(SocketMessage msg)
      => _executeRule.Check(msg);

    public Task ExecuteCommand(SocketMessage msg)
    {
      return Task.Run(async () =>
      {
        try
        {
          await _cmd(msg).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          ex.LogToConsole($"[{_stringID.ToUpper(CultureInfo.InvariantCulture)}] Command failed.");
        }
      });
    }
  }
}
