using System;
using System.Collections.Generic;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace CozyBot
{
    struct CanvasData
    {
        public int Width;
        public int Height;
        public Image<Rgba32> Canvas;
        public Dictionary<byte, Rgba32> Palette;

        public Rgba32 this[int x, int y] => Canvas[x, y];
    }
}
