#:package SkiaSharp@2.88.9
// Generates src/DiscCheck.App/Assets/Icons/disccheck.ico (Requirements 32.8-32.10).
//
// Sources (read at generation time, never copied into DiscCheck):
//   - src/DAS.Design/Assets/Logo/das-mark-color.svg  (DAS_Logo monogram, unchanged)
//   - src/DAS.Design/Assets/Icons/das.status.ok.svg  (check mark of the badge)
// Output: 7 sizes (16, 24, 32, 48, 64, 128, 256 px), PNG-compressed, with a badge at the bottom right:
//   a circle #0B7A75 (Accent Light) with a white check, diameter 40% of the icon side (50% for 16 and 24 px).
//
// Usage (from any folder inside the repository):  dotnet run src/DiscCheck.App/Assets/generate-icon.cs
// Optional argument: output path. See Assets/README.md.

using System.Text.RegularExpressions;
using System.Xml.Linq;
using SkiaSharp;

int[] sizes = [16, 24, 32, 48, 64, 128, 256];

var root = new DirectoryInfo(Directory.GetCurrentDirectory());
while (root is not null && !File.Exists(Path.Combine(root.FullName, "DiscCheck.sln")))
{
    root = root.Parent;
}

if (root is null)
{
    Console.Error.WriteLine("DiscCheck.sln not found: run the script from inside the repository.");
    return 1;
}

var markPath = Path.Combine(root.FullName, "src", "DAS.Design", "Assets", "Logo", "das-mark-color.svg");
var okPath = Path.Combine(root.FullName, "src", "DAS.Design", "Assets", "Icons", "das.status.ok.svg");
var outPath = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.Combine(root.FullName, "src", "DiscCheck.App", "Assets", "Icons", "disccheck.ico");

var mark = XDocument.Load(markPath).Root!;
var markBox = Regex.Matches((string)mark.Attribute("viewBox")!, @"[\d.]+").Select(m => float.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
var markSide = markBox[2];

// The check is the second sub-path of das.status.ok (the first one is the ring, not used by the badge).
var okD = (string)XDocument.Load(okPath).Root!.Descendants().First(e => e.Name.LocalName == "path").Attribute("d")!;
var checkD = okD[(okD.IndexOf('z') + 1)..];
using var checkPath = SKPath.ParseSvgPathData(checkD);
var checkBounds = checkPath.Bounds;

static SKColor Hex(string value) => SKColor.Parse(value);

byte[] Render(int size)
{
    using var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
    using var canvas = new SKCanvas(bitmap);
    canvas.Clear(SKColors.Transparent);

    canvas.Save();
    canvas.Scale(size / markSide);
    foreach (var e in mark.Elements())
    {
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = Hex((string)e.Attribute("fill")!) };
        switch (e.Name.LocalName)
        {
            case "rect":
                var rx = (float?)e.Attribute("rx") ?? 0;
                canvas.DrawRoundRect(new SKRect(0, 0, (float)e.Attribute("width")!, (float)e.Attribute("height")!), rx, rx, paint);
                break;
            case "path":
                using (var p = SKPath.ParseSvgPathData((string)e.Attribute("d")!))
                {
                    canvas.DrawPath(p, paint);
                }

                break;
            case "circle":
                canvas.DrawCircle((float)e.Attribute("cx")!, (float)e.Attribute("cy")!, (float)e.Attribute("r")!, paint);
                break;
        }
    }

    canvas.Restore();

    // Badge: bottom-right, flush with the icon edge.
    var ratio = size <= 24 ? 0.5f : 0.4f;
    var d = size * ratio;
    var r = d / 2;
    var cx = size - r;
    var cy = size - r;
    using (var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = Hex("#0B7A75") })
    {
        canvas.DrawCircle(cx, cy, r, fill);
    }

    // White check scaled so that it spans ~60% of the badge diameter, centred.
    var k = d * 0.6f / checkBounds.Width;
    canvas.Save();
    canvas.Translate(cx - checkBounds.MidX * k, cy - checkBounds.MidY * k);
    canvas.Scale(k);
    using (var stroke = new SKPaint
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        Color = SKColors.White,
        StrokeWidth = Math.Max(1.5f, d * 0.12f) / k,
        StrokeCap = SKStrokeCap.Round,
        StrokeJoin = SKStrokeJoin.Round,
    })
    {
        canvas.DrawPath(checkPath, stroke);
    }

    canvas.Restore();

    using var image = SKImage.FromBitmap(bitmap);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

var images = sizes.Select(s => (Size: s, Png: Render(s))).ToList();

Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
using (var fs = File.Create(outPath))
using (var w = new BinaryWriter(fs))
{
    w.Write((ushort)0);
    w.Write((ushort)1);
    w.Write((ushort)images.Count);
    var offset = 6 + 16 * images.Count;
    foreach (var (size, png) in images)
    {
        w.Write((byte)(size >= 256 ? 0 : size));
        w.Write((byte)(size >= 256 ? 0 : size));
        w.Write((byte)0);
        w.Write((byte)0);
        w.Write((ushort)1);
        w.Write((ushort)32);
        w.Write(png.Length);
        w.Write(offset);
        offset += png.Length;
    }

    foreach (var (_, png) in images)
    {
        w.Write(png);
    }
}

Console.WriteLine($"Written {outPath} ({string.Join(", ", sizes)} px).");
return 0;
