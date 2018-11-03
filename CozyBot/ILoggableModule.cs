using System;
using System.Collections.Generic;
using System.Text;

namespace DiscordBot1
{
    public interface ILoggableModule
    {
        event ModuleLogged ModuleLogged;
    }
}
