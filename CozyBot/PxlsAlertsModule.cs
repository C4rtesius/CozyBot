using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Globalization;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Discord;
using Discord.WebSocket;

using SixLabors.ImageSharp;
using SixLabors.Primitives;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace CozyBot
{
    public class PxlsAlertsModule : IGuildModule
    {
        private static string _stringID => "PxlsAlertsModule";
        private static string _moduleXmlName => "pxls-alerts";
        private static string _moduleFolder => @"pxls-alerts";

        private SocketGuild _guild;

        protected XElement _configEl;
        protected List<ulong> _adminIds;
        protected bool _isActive;
        protected List<IBotCommand> _useCommands = new List<IBotCommand>();
        //protected event ConfigChanged _configChanged;
        protected static string _defaultPrefix = "p!";
        protected string _guildPath;
        protected string _prefix;


        static string _infoDataUrl = @"https://pxls.space/info";
        static string _boardDataUrl = @"https://pxls.space/boarddata";
        static string _templateRegexPattern = @"template=(?<uri>https?[\:\w\d%\.\/]+(?:%2F|\/)(?<file>[\w\d]+))";
        static string _offsetxRegexPattern = @"ox=([0-9]+)\b";
        static string _offsetyRegexPattern = @"oy=([0-9]+)\b";

        static HttpClient _hc = new HttpClient();

        private string _modulePath => Path.Combine(_guildPath, _moduleFolder);

        public string StringID => _stringID;
        public SocketGuild Guild => _guild;
        public bool IsActive => _isActive;
        public string ModuleXmlName => _moduleXmlName;

        public event ConfigChanged GuildBotConfigChanged
        {
            add
            {
                if (value != null)
                {
                    //_configChanged += value;
                }
            }
            remove
            {
                if (value != null)
                {
                    //_configChanged -= value;
                }
            }
        }

        public IEnumerable<IBotCommand> ActiveCommands
        {
            get
            {
                foreach (var cmd in _useCommands)
                    yield return cmd;
            }
        }

        public PxlsAlertsModule(XElement configEl, List<ulong> adminIds, SocketGuild guild, string guildPath)
        {
            _guild = Guard.NonNull(guild, nameof(guild));
            _configEl = Guard.NonNull(configEl, nameof(configEl));
            _guildPath = Guard.NonNullWhitespaceEmpty(guildPath, nameof(guildPath));
            _adminIds = adminIds;
            
            if (_configEl.Element(ModuleXmlName) == null)
            {
                XElement moduleConfigEl =
                    new XElement(ModuleXmlName,
                        new XAttribute("on", Boolean.FalseString),
                        new XAttribute("prefix", _defaultPrefix)
                    );

                _configEl.Add(moduleConfigEl);
            }

            if (!Directory.Exists(_guildPath))
            {
                Directory.CreateDirectory(_guildPath);
            }

            if (!Directory.Exists(_modulePath))
                Directory.CreateDirectory(_modulePath);

            Configure(_configEl);
        }

        protected void Configure(XElement configEl)
        {
            XElement guildBotModuleCfg = configEl.Element(ModuleXmlName);

            bool isActive = false;

            if (guildBotModuleCfg.Attribute("on") != null)
            {
                if (!Boolean.TryParse(guildBotModuleCfg.Attribute("on").Value, out isActive))
                {
                    isActive = false;
                }
            }

            _isActive = isActive;

            string prefix = _defaultPrefix;

            if (guildBotModuleCfg.Attribute("prefix") != null)
            {
                if (!String.IsNullOrWhiteSpace(guildBotModuleCfg.Attribute("prefix").Value))
                {
                    prefix = guildBotModuleCfg.Attribute("prefix").Value;
                }
            }

            _prefix = prefix;

            if (_isActive)
                GenerateUseCommands();
            else
                _useCommands = new List<IBotCommand>();
        }

        public void Reconfigure(XElement configEl)
        {
            if (configEl == null)
                return;
            Configure(configEl);
        }

        private void GenerateUseCommands()
        {
            var list = new List<IBotCommand>();

            list.Add(
                new BotCommand(
                    StringID + "-status",
                    RuleGenerator.PrefixatedCommand(_prefix, "status"),
                    StatusCmd
                )
            );

            _useCommands = list;
        }

        private async Task StatusCmd(SocketMessage msg)
        {
#if DEBUG
            Console.WriteLine("[DEBUG][PXLS-ALERTS] Entered StatusCmd.");
#endif
            string[] words = msg.Content.Split(" ");
            string _inputUri = words[1];

#if DEBUG
            Console.WriteLine($"[DEBUG][PXLS-ALERTS] words[1] :{words[1]}");
#endif
            if (!_inputUri.StartsWith(@"https://pxls.space/#"))
                return;

            var templateRegex = new Regex(_templateRegexPattern, RegexOptions.Compiled);
            var offsetxRegex = new Regex(_offsetxRegexPattern, RegexOptions.Compiled);
            var offsetyRegex = new Regex(_offsetyRegexPattern, RegexOptions.Compiled);
            
            var templateMatch = templateRegex.Match(_inputUri);
            var offsetxString = offsetxRegex.Match(_inputUri).Groups[1].Value;
            var offsetyString = offsetyRegex.Match(_inputUri).Groups[1].Value;

            if (!Uri.TryCreate(templateMatch.Groups["uri"].Value, UriKind.RelativeOrAbsolute, out Uri uri))
#if DEBUG
            {
                Console.WriteLine($"[DEBUG][PXLS-ALERTS] Invalid uri :{templateMatch.Groups["uri"].Value}");
                return;
            }
#else
                return;
#endif
            var output = templateMatch.Groups["file"].Value;
            
            if (String.IsNullOrEmpty(output) || String.IsNullOrWhiteSpace(output))
#if DEBUG
            {
                Console.WriteLine($"[DEBUG][PXLS-ALERTS] Invalid output :{output}");
                return;
            }
#else
                return;
#endif
            if (!Int32.TryParse(offsetxString, out int offsetx))
#if DEBUG
            {
                Console.WriteLine($"[DEBUG][PXLS-ALERTS] Invalid offsetx :{offsetxString}");
                return;
            }
#else
                return;
#endif
            if (!Int32.TryParse(offsetyString, out int offsety))
#if DEBUG
            {
                Console.WriteLine($"[DEBUG][PXLS-ALERTS] Invalid offsety :{offsetxString}");
                return;
            }
#else
                return;
#endif

#if DEBUG
            Console.WriteLine($"[DEBUG][PXLS-ALERTS] Checks passed.");
#endif
            string rawTemplatePath = Path.Combine(_modulePath, output);

            try
            {
                using var response = await _hc.GetAsync(Uri.UnescapeDataString($"{uri}"));
                using var cs = await response.Content.ReadAsStreamAsync();
                using (var fs = new FileStream(rawTemplatePath, FileMode.Create, FileAccess.ReadWrite))
                    await cs.CopyToAsync(fs);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                return;
            }

#if DEBUG
            Console.WriteLine($"[DEBUG][PXLS-ALERTS] Template downloaded.");
#endif
            SixLabors.ImageSharp.Image<Rgba32> template;
            try
            {
                template = SixLabors.ImageSharp.Image.Load(Path.Combine(_modulePath, output)).CloneAs<Rgba32>();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                return;
            }

            CanvasData canvasData;

            try
            {
                canvasData = await GetCanvasData(_infoDataUrl, _boardDataUrl);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                return;
            }
#if DEBUG
            Console.WriteLine($"[DEBUG][PXLS-ALERTS] Canvas data downloaded.");
#endif

            int symbolSize = GetTemplateSymbolSizeFromMetadata(template);
            if (symbolSize == 0)
                symbolSize = GetTemplateSymbolSize(template, canvasData.Palette.Values);
            else
                template[0, 0] = new Rgba32(template[0, 0].R, template[0, 0].G, template[0, 0].B, (template[0, 0].A > 128) ? 255 : 0);

            var img = Detemplatize(template, symbolSize, canvasData.Palette.Values);
            
            string pngImagePath = Path.Combine(_modulePath, $"{output}.png");
            try
            {
                using (var fs = new FileStream(pngImagePath, FileMode.Create, FileAccess.ReadWrite))
                    img.SaveAsPng(fs);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                return;
            }

#if DEBUG
            Console.WriteLine($"[DEBUG][PXLS-ALERTS] Image detemplatized.");
#endif

            int totalPxls = 0;
            int wrongPxls = 0;

            var transparent = new Rgba32(0, 0, 0, 0);

            int xLow;
            int xHigh;
            int yLow;
            int yHigh;

            if (offsetx < 0)
            {
                xLow = -offsetx;
                if (offsetx + img.Width < 0)
                    return;
                else if (offsetx + img.Width >= canvasData.Width)
                    xHigh = img.Width - canvasData.Width + offsetx;
                else
                    xHigh = img.Width;
            }
            else if (offsetx >= canvasData.Width)
            {
                return;
            }
            else
            {
                xLow = 0;
                if (offsetx + img.Width < canvasData.Width)
                    xHigh = img.Width;
                else
                    xHigh = canvasData.Width - offsetx;
            }

            if (offsety < 0)
            {
                yLow = -offsety;
                if (offsety + img.Height < 0)
                    return;
                else if (offsety + img.Height >= canvasData.Height)
                    yHigh = img.Height - canvasData.Height + offsety;
                else
                    yHigh = img.Height;
            }
            else if (offsety >= canvasData.Height)
                return;
            else
            {
                yLow = 0;
                if (offsety + img.Height < canvasData.Height)
                    yHigh = img.Height;
                else
                    yHigh = canvasData.Height - offsety;
            }

            using var wrongMap = new Image<Rgba32>(Configuration.Default, xHigh - xLow, yHigh - yLow, Rgba32.Transparent);

            for (int x = 0; x < xHigh - xLow; x++)
            {
                for (int y = 0; y < yHigh - yLow; y++)
                {
                    if (img[x + xLow, y + yLow] != transparent)
                    {
                        totalPxls++;
                        if (canvasData[offsetx + x + xLow, offsety + y + yLow] != img[x + xLow, y + yLow])
                        {
                            wrongPxls++;
                            wrongMap[x, y] = Rgba32.Red;
                        }
                    }
                }
            }

#if DEBUG
            Console.WriteLine($"[DEBUG][PXLS-ALERTS] Wrongmap formed.");
#endif

            float amountC = 0.7f;
            float amountT = 0.7f;
            int border = 20;
            using var res = canvasData.Canvas.Clone();

            if (offsetx < 0)
            {
                xLow = 0;
                if (wrongMap.Width + border >= canvasData.Width)
                    xHigh = canvasData.Width;
                else
                    xHigh = wrongMap.Width + border;
            }
            else if (offsetx - border < 0)
            {
                xLow = 0;
                if (wrongMap.Width + border + offsetx >= canvasData.Width)
                    xHigh = canvasData.Width;
                else
                    xHigh = wrongMap.Width + border + offsetx;
            }
            else
            {
                xLow = offsetx - border;
                if (wrongMap.Width + border + offsetx >= canvasData.Width)
                    xHigh = border - offsetx + canvasData.Width;
                else
                    xHigh = 2 * border + wrongMap.Width;
            }

            if (offsety < 0)
            {
                yLow = 0;
                if (wrongMap.Height + border >= canvasData.Height)
                    yHigh = canvasData.Height;
                else
                    yHigh = wrongMap.Height + border;
            }
            else if (offsety - border < 0)
            {
                yLow = 0;
                if (wrongMap.Height + border + offsety >= canvasData.Height)
                    yHigh = canvasData.Height;
                else
                    yHigh = wrongMap.Height + border + offsety;
            }
            else
            {
                yLow = offsety - border;
                if (wrongMap.Height + border + offsety >= canvasData.Height)
                    yHigh = border - offsety + canvasData.Height;
                else
                    yHigh = 2 * border + wrongMap.Height;
            }

            var newSize = (xHigh > yHigh) ? new Size(800, (int)(yHigh * 800f / xHigh)) : new Size((int)(xHigh * 800f / yHigh), 800);
            var resampler = (xHigh > yHigh) ? ((xHigh > 800) ? KnownResamplers.Bicubic : KnownResamplers.NearestNeighbor) :
                (yHigh > 800) ? KnownResamplers.Bicubic : KnownResamplers.NearestNeighbor;

            GraphicsOptions go = new GraphicsOptions { ColorBlendingMode = PixelColorBlendingMode.Subtract, BlendPercentage = amountT };
            ResizeOptions ro = new ResizeOptions { Mode = ResizeMode.Stretch, Position = AnchorPositionMode.Center, Sampler = resampler, Size = newSize };

            res.Mutate(
                o =>
                    o.Brightness(amountC)
                    .DrawImage(img, new Point(offsetx, offsety), go)
                    .DrawImage(wrongMap, new Point((offsetx < 0) ? 0 : offsetx, (offsety < 0) ? 0 : offsety), 1.0f)
                    .Crop(new Rectangle(xLow, yLow, xHigh, yHigh))
                    .Resize(ro)
            );

#if DEBUG
            Console.WriteLine($"[DEBUG][PXLS-ALERTS] Wrongmap image processed and formed.");
#endif
            string wrongMapFilePath = Path.Combine(_modulePath, $"{output}_wrongmap.png");

            try
            {
                using (var fs = new FileStream(wrongMapFilePath, FileMode.Create, FileAccess.ReadWrite))
                    res.SaveAsPng(fs);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                return;
            }

#if DEBUG
            Console.WriteLine($"[DEBUG][PXLS-ALERTS] Wrongmap image saved.");
#endif
            try
            {
                string outMsg = $"```{wrongPxls} incorrect from {totalPxls} total.{Environment.NewLine}Percent done : {100 - 100f * wrongPxls / totalPxls}```";
                await msg.Channel.SendMessageAsync(outMsg);
                await msg.Channel.SendFileAsync(wrongMapFilePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                return;
            }
#if DEBUG
            Console.WriteLine($"[DEBUG][PXLS-ALERTS] Message and image sent.");
#endif
            try
            {
                File.Delete(rawTemplatePath);
                File.Delete(pngImagePath);
                File.Delete(wrongMapFilePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                return;
            }
#if DEBUG
            Console.WriteLine($"[DEBUG][PXLS-ALERTS] Temp files deleted.");
#endif
        }

        static async Task<CanvasData> GetCanvasData(string infoUri, string boardUri)
        {
            var paletteRegex = new Regex(@"^#(?<r>[0-9a-fA-F]{2})(?<g>[0-9a-fA-F]{2})(?<b>[0-9a-fA-F]{2})$", RegexOptions.Compiled);
            var paletteDict = new Dictionary<byte, Rgba32>();
            int width = 0;
            int height = 0;

            var response = await _hc.GetAsync(infoUri);
            response.EnsureSuccessStatusCode();
            var resultStream = await response.Content.ReadAsStreamAsync();
            var jsonRoot = (await JsonDocument.ParseAsync(resultStream)).RootElement;

            if (jsonRoot.TryGetProperty("palette", out var paletteVal))
            {
                byte count = 0;
                foreach (var el in paletteVal.EnumerateArray())
                {
                    var elStrVal = el.GetString();
                    var match = paletteRegex.Match(elStrVal);

                    if (byte.TryParse(match.Groups["r"].Value, NumberStyles.HexNumber, null, out byte r))
                        if (byte.TryParse(match.Groups["g"].Value, NumberStyles.HexNumber, null, out byte g))
                            if (byte.TryParse(match.Groups["b"].Value, NumberStyles.HexNumber, null, out byte b))
                                paletteDict.Add(count++, new Rgba32(r, g, b));
                }
                paletteDict.Add(255, new Rgba32(0, 0, 0, 0));
            }

            if (jsonRoot.TryGetProperty("width", out var widthEl))
            {
                if (widthEl.TryGetInt32(out int widthVal))
                {
                    width = widthVal;
                }
            }

            if (jsonRoot.TryGetProperty("width", out var heightEl))
            {
                if (heightEl.TryGetInt32(out int heightVal))
                {
                    height = heightVal;
                }
            }

            response = await _hc.GetAsync(boardUri);
            response.EnsureSuccessStatusCode();
            var canvasData = await response.Content.ReadAsByteArrayAsync();
            var boardData = new Image<Rgba32>(width, height);
            for (int i = 0; i < width; i++)
                for (int j = 0; j < height; j++)
                    boardData[i, j] = paletteDict[canvasData[i + j * width]];

            return new CanvasData { Width = width, Height = height, Palette = paletteDict, Canvas = boardData };
        }

        static Image<Rgba32> Detemplatize(Image<Rgba32> src, int symbolSize, IEnumerable<Rgba32> palette)
        {
            if (symbolSize == 1)
                return src.Clone();

            var outImg = new Image<Rgba32>(src.Width / symbolSize, src.Height / symbolSize);

            for (int i = 0; i < src.Width; i += symbolSize)
            {
                for (int j = 0; j < src.Height; j += symbolSize)
                {
                    outImg[i / symbolSize, j / symbolSize] = GetColour(src, symbolSize, i, j, palette);
                }
            }

            return outImg;
        }

        static int GetTemplateSymbolSizeFromMetadata(Image<Rgba32> template)
        {
            int a = template[0, 0].A;

            if (a > 128)
                return 255 - a;
            else
                return a;
        }

        static int GetTemplateSymbolSize(Image<Rgba32> template, IEnumerable<Rgba32> palette)
        {
            Rgba32 c = Rgba32.Transparent;

            for (int tempSize = 21; tempSize > 2; tempSize--)
            {
                if ((template.Width % tempSize != 0) || (template.Height % tempSize != 0))
                    continue;

                for (int i = 0; i < template.Width; i += tempSize)
                {
                    for (int j = 0; j < template.Height; j += tempSize)
                    {
                        c = GetColour(template, tempSize, i, j, palette);
                        if (c == Rgba32.Transparent)
                            break;
                    }
                    if (c == Rgba32.Transparent)
                        break;
                }

                if (c != Rgba32.Transparent)
                    return tempSize;
            }

            return 1;
        }

        static Rgba32 GetColour(Image<Rgba32> template, int tempSize, int i, int j, IEnumerable<Rgba32> palette)
        {
            Rgba32 refColour = Rgba32.Transparent;
            bool trFlag = true;
            for (int ii = 0; ii < tempSize; ii++)
            {
                for (int jj = 0; jj < tempSize; jj++)
                {
                    var c = template[i + ii, j + jj];
                    if (c.A == 0)
                        continue;
                    if (!palette.Contains(c))
                        continue;

                    trFlag = false;
                    
                    if (refColour == Rgba32.Transparent)
                    {
                        refColour = c;
                    }
                    else if (c != refColour)
                    {
                        return Rgba32.Transparent;
                    }
                }
            }

            if (trFlag)
                refColour = new Rgba32(0, 0, 0, 0);

            return refColour;
        }
    }
}
