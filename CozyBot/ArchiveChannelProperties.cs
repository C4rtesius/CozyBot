using System;
using System.Collections.Generic;
using System.Text;

namespace CozyBot
{
    public class ArchiveChannelProperties
    {
        public ulong Id;
        public DateTime Last;
        public bool Timer;
        public int Interval;
        public bool Image;
        public string Filepath;
        public bool Silent;

        public ArchiveChannelProperties(ulong id, DateTime last, bool timer, int interval, bool image, string filepath, bool silent)
        {
            Id = id;
            Last = last;
            Timer = timer;
            Interval = interval;
            Image = image;
            Filepath = filepath;
            Silent = silent;
        }
    }
}
