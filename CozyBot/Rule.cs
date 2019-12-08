using System;
using System.Collections.Generic;
using System.Text;

using Discord.WebSocket;

namespace CozyBot
{
    public class Rule
    {
        //Private Fields
        private readonly Predicate<SocketMessage> _rule;

        private static readonly Predicate<SocketMessage> _truePredicate = (_) => true;

        public static Rule TrueRule { get; private set; }

        public static Rule FalseRule { get; private set; }

        static Rule()
        {
            TrueRule  = new Rule(_truePredicate);
            FalseRule = new Rule((_) => false);
        }

        public Rule(Predicate<SocketMessage> rule)
        {
            if (rule == null)
                _rule = _truePredicate;
            else
                _rule = rule;
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

            return new Rule(
                (msg) => rule1._rule(msg) && rule2._rule(msg));
        }

        public static Rule operator |(Rule rule1, Rule rule2)
        {
            if (rule1 == TrueRule || rule2 == TrueRule)
                return TrueRule;

            if (rule1 == FalseRule)
                return rule2;

            if (rule2 == FalseRule)
                return rule1;

            return new Rule(
                (msg) => rule1._rule(msg) || rule2._rule(msg));
        }

        public static Rule operator ^(Rule rule1, Rule rule2)
        {
            if (rule1 == rule2)
                return FalseRule;

            return new Rule(
                (msg) => rule1._rule(msg) ^ rule2._rule(msg));
        }

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
