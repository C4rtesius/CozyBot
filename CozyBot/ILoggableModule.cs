using System;
using System.Collections.Generic;
using System.Text;

namespace CozyBot
{
    public interface ILoggableModule
    {
        event ModuleLogged ModuleLogged;
    }
}
