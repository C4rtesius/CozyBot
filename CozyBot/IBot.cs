using System;
using System.Collections.Generic;
using System.Text;

namespace CozyBot
{
    interface IBot
    {
        Dictionary<string, IBotModule> ModulesDict { get; }
    }
}
