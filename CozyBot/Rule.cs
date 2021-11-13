using System;

using Discord.WebSocket;

namespace CozyBot
{
  public class Rule
  {
    private static readonly Predicate<SocketMessage> _truePredicate = (_) => true;

    private readonly Predicate<SocketMessage> _rule;

    public static Rule TrueRule { get; }
    public static Rule FalseRule { get; }

    static Rule()
    {
      TrueRule = new Rule(_truePredicate);
      FalseRule = new Rule((_) => false);
    }

    public Rule(Predicate<SocketMessage> rule)
    {
      _rule = rule ?? _truePredicate;
    }

    public bool Check(SocketMessage msg)
      => _rule(msg);

    public static Rule operator &(Rule rule1, Rule rule2)
    {
      if (rule1 == FalseRule || rule2 == FalseRule)
        return FalseRule;

      if (rule1)
        return rule2;

      if (rule2)
        return rule1;

      return new Rule((msg) => rule1._rule(msg) && rule2._rule(msg));
    }

    public static Rule operator |(Rule rule1, Rule rule2)
    {
      if (rule1 == TrueRule || rule2 == TrueRule)
        return TrueRule;

      if (rule1 == FalseRule)
        return rule2;

      if (rule2 == FalseRule)
        return rule1;

      return new Rule((msg) => rule1._rule(msg) || rule2._rule(msg));
    }

    public static Rule operator ^(Rule rule1, Rule rule2)
      => (rule1 == rule2) ? FalseRule : new Rule((msg) => rule1._rule(msg) ^ rule2._rule(msg));

    public static Rule operator !(Rule rule)
    {
      if (rule == FalseRule)
        return TrueRule;

      if (rule == TrueRule)
        return FalseRule;

      return new Rule((msg) => !rule._rule(msg));
    }

    public static bool operator true(Rule rule)
      => rule == TrueRule;

    public static bool operator false(Rule rule)
      => rule == FalseRule;

  }
}
