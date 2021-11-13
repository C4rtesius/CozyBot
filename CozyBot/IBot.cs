using System.Collections.Generic;

namespace CozyBot
{
  interface IBot
  {
    Dictionary<string, IBotModule> ModulesDict { get; }
  }
}
