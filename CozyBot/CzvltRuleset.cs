using System;
using System.Collections.Generic;
using System.Text;

namespace DiscordBot1
{
    static class CzvltRuleset
    {
        private static ulong _didRoleId = 334775752623128576UL;
        private static Rule _didRule = null;

        public static Rule DidRule
        {
            get
            {
                return _didRule;
            }
        }

        static CzvltRuleset()
        {
            _didRule = RuleGenerator.RoleByID(_didRoleId);
        }
    }
}
