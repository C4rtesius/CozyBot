using System;

namespace CozyBot
{
  public class ArchiveChannelProperties
  {
    public ulong Id { get; }
    public DateTime Last { get; }
    public bool Timer { get; }
    public int Interval { get; }
    public bool Image { get; }
    public string Filepath { get; }
    public bool Silent { get; }

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
