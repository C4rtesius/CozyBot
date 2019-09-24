using System;
using System.Collections.Generic;
using System.Text;

using Discord.WebSocket;

namespace CozyBot
{
    public class Rule
    {
        //Private Fields
        private Predicate<SocketMessage> _rule;

        private static Rule _trueRule;
        private static Rule _falseRule;

        public static Rule TrueRule
        {
            get
            {
                return _trueRule;
            }
        }

        public static Rule FalseRule
        {
            get
            {
                return _falseRule;
            }
        }

        static Rule()
        {
            _trueRule = new Rule((msg) => { return true; });
            _falseRule = new Rule((msg) => { return false; });
        }

        public Rule(Predicate<SocketMessage> rule)
        {
            if (rule == null)
            {
                _rule = (msg) => { return true; };
            }

            _rule = rule;
        }

        public bool Check(SocketMessage msg)
        {
            return _rule(msg);
        }

        public static Rule operator& (Rule rule1, Rule rule2)
        {
            if (rule1 == _falseRule
                || rule2 == _falseRule)
            {
                return _falseRule;
            }

            if (rule1 == _trueRule)
            {
                return rule2;
            }

            if (rule2 == _trueRule)
            {
                return rule1;
            }

            return new Rule (
                (msg) =>
                {
                    return rule1._rule(msg) && rule2._rule(msg);
                }
            );
        }

        public static Rule operator| (Rule rule1, Rule rule2)
        {
            if (rule1 == _trueRule 
                || rule2 == _trueRule)
            {
                return _trueRule;
            }

            if (rule1 == _falseRule)
            {
                return rule2;
            }

            if (rule2 == _falseRule)
            {
                return rule1;
            }

            return new Rule(
                (msg) =>
                {
                    return rule1._rule(msg) || rule2._rule(msg);
                }
            );
        }

        public static Rule operator! (Rule rule)
        {
            if (rule == _falseRule)
            {
                return _trueRule;
            }

            if (rule == _trueRule)
            {
                return _falseRule;
            }

            return new Rule((msg) => { return !rule._rule(msg); });
        }

        public static bool operator true(Rule rule)
        {
            return rule == _trueRule;
        }

        public static bool operator false(Rule rule)
        {
            return rule == _falseRule;
        }

    }
}
