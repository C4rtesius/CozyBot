using System;
using System.Xml.Linq;

namespace CozyBot
{
  public class ConfigChangedEventArgs : EventArgs
  {
    public XElement NewConfigElement { get; }

    public ConfigChangedEventArgs(XElement newConfigEl)
    {
      NewConfigElement = newConfigEl ?? throw new ArgumentNullException(nameof(newConfigEl));
    }
  }
}
