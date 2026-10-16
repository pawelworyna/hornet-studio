#pragma warning disable CA1416 // System.Drawing is used here on Windows to generate build assets
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using FluentAssertions;

namespace HornetStudio.Tests.SampleData;

public class GreenHornetIconGenerator
{
    [Fact]
    public void GenerateAndVerifyApplicationIcons()
    {
        var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var assetsDir = Path.Combine(projectRoot, "src", "HornetStudio.Desktop", "Assets");
        Directory.CreateDirectory(assetsDir);

        int[] sizes = [16, 24, 32, 48, 64, 128, 256];
        var pngBytesList = new List<byte[]>();

        foreach (var size in sizes)
        {
            using var bmp = RenderHornetBitmap(size);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            var bytes = ms.ToArray();
            pngBytesList.Add(bytes);

            if (size == 256)
            {
                var pngPath = Path.Combine(assetsDir, "hornet.png");
                File.WriteAllBytes(pngPath, bytes);
            }
        }

        // Build multi-resolution ICO file
        var icoPath = Path.Combine(assetsDir, "hornet.ico");
        using (var fs = new FileStream(icoPath, FileMode.Create, FileAccess.Write))
        using (var writer = new BinaryWriter(fs))
        {
            // ICO Header
            writer.Write((ushort)0); // Reserved
            writer.Write((ushort)1); // Type: 1 = ICO
            writer.Write((ushort)sizes.Length); // Image count

            uint offset = (uint)(6 + 16 * sizes.Length);

            for (int i = 0; i < sizes.Length; i++)
            {
                var size = sizes[i];
                var data = pngBytesList[i];
                byte w = size >= 256 ? (byte)0 : (byte)size;
                byte h = size >= 256 ? (byte)0 : (byte)size;

                writer.Write(w);
                writer.Write(h);
                writer.Write((byte)0); // Color palette
                writer.Write((byte)0); // Reserved
                writer.Write((ushort)1); // Color planes
                writer.Write((ushort)32); // Bits per pixel
                writer.Write((uint)data.Length); // Image size in bytes
                writer.Write(offset); // Offset to image data

                offset += (uint)data.Length;
            }

            foreach (var data in pngBytesList)
            {
                writer.Write(data);
            }
        }

        // Also copy over avalonia-logo.ico so any legacy references use the hornet
        var avaloniaLogoIco = Path.Combine(assetsDir, "avalonia-logo.ico");
        File.Copy(icoPath, avaloniaLogoIco, overwrite: true);

        // Verification
        File.Exists(icoPath).Should().BeTrue();
        new FileInfo(icoPath).Length.Should().BeGreaterThan(1000);

        var hornetPngPath = Path.Combine(assetsDir, "hornet.png");
        File.Exists(hornetPngPath).Should().BeTrue();
        new FileInfo(hornetPngPath).Length.Should().BeGreaterThan(1000);
    }

    private static Bitmap RenderHornetBitmap(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        float s = size / 256.0f;
        float center = size / 2.0f;
        float radius = size * 0.46f;

        // 1. Dark Hexagonal Badge Background
        var hexPoints = new PointF[]
        {
            new(center, center - radius),
            new(center + radius * 0.866f, center - radius * 0.5f),
            new(center + radius * 0.866f, center + radius * 0.5f),
            new(center, center + radius),
            new(center - radius * 0.866f, center + radius * 0.5f),
            new(center - radius * 0.866f, center - radius * 0.5f)
        };

        using (var bgBrush = new LinearGradientBrush(
            new RectangleF(0, 0, size, size),
            Color.FromArgb(255, 11, 15, 20),
            Color.FromArgb(255, 6, 78, 59),
            LinearGradientMode.ForwardDiagonal))
        {
            g.FillPolygon(bgBrush, hexPoints);
        }

        // Emerald Outer Border
        using (var borderPen = new Pen(Color.FromArgb(255, 16, 185, 129), Math.Max(1.5f, 4.0f * s)))
        {
            g.DrawPolygon(borderPen, hexPoints);
        }

        // Inner Glow Border
        float innerRadius = radius * 0.92f;
        var innerHex = new PointF[]
        {
            new(center, center - innerRadius),
            new(center + innerRadius * 0.866f, center - innerRadius * 0.5f),
            new(center + innerRadius * 0.866f, center + innerRadius * 0.5f),
            new(center, center + innerRadius),
            new(center - innerRadius * 0.866f, center + innerRadius * 0.5f),
            new(center - innerRadius * 0.866f, center - innerRadius * 0.5f)
        };
        using (var innerPen = new Pen(Color.FromArgb(70, 52, 211, 153), Math.Max(1.0f, 1.5f * s)))
        {
            g.DrawPolygon(innerPen, innerHex);
        }

        // 2. Wings (Translucent Emerald Gradient)
        var leftWingRect = new RectangleF(center - 95 * s, center - 60 * s, 90 * s, 90 * s);
        using (var wingBrushLeft = new LinearGradientBrush(
            leftWingRect,
            Color.FromArgb(210, 52, 211, 153),
            Color.FromArgb(100, 16, 185, 129),
            LinearGradientMode.Horizontal))
        {
            var leftWingPts = new PointF[]
            {
                new(center - 15 * s, center - 12 * s),
                new(center - 88 * s, center - 58 * s),
                new(center - 76 * s, center + 14 * s),
                new(center - 20 * s, center + 20 * s)
            };
            g.FillPolygon(wingBrushLeft, leftWingPts);
        }

        var rightWingRect = new RectangleF(center, center - 60 * s, 90 * s, 90 * s);
        using (var wingBrushRight = new LinearGradientBrush(
            rightWingRect,
            Color.FromArgb(100, 16, 185, 129),
            Color.FromArgb(210, 52, 211, 153),
            LinearGradientMode.Horizontal))
        {
            var rightWingPts = new PointF[]
            {
                new(center + 15 * s, center - 12 * s),
                new(center + 88 * s, center - 58 * s),
                new(center + 76 * s, center + 14 * s),
                new(center + 20 * s, center + 20 * s)
            };
            g.FillPolygon(wingBrushRight, rightWingPts);
        }

        // Wing Veins
        using (var veinPen = new Pen(Color.FromArgb(200, 241, 245, 249), Math.Max(1.0f, 1.5f * s)))
        {
            g.DrawLine(veinPen, center - 15 * s, center - 12 * s, center - 84 * s, center - 52 * s);
            g.DrawLine(veinPen, center + 15 * s, center - 12 * s, center + 84 * s, center - 52 * s);
        }

        // 3. Antennae
        using (var antPen = new Pen(Color.FromArgb(255, 52, 211, 153), Math.Max(1.5f, 2.5f * s)))
        {
            antPen.StartCap = LineCap.Round;
            antPen.EndCap = LineCap.Round;
            g.DrawBezier(antPen,
                center - 6 * s, center - 54 * s,
                center - 22 * s, center - 76 * s,
                center - 36 * s, center - 84 * s,
                center - 46 * s, center - 78 * s);

            g.DrawBezier(antPen,
                center + 6 * s, center - 54 * s,
                center + 22 * s, center - 76 * s,
                center + 36 * s, center - 84 * s,
                center + 46 * s, center - 78 * s);
        }

        // 4. Head
        using (var headBrush = new SolidBrush(Color.FromArgb(255, 16, 185, 129)))
        {
            var headPts = new PointF[]
            {
                new(center, center - 62 * s),
                new(center + 18 * s, center - 44 * s),
                new(center, center - 34 * s),
                new(center - 18 * s, center - 44 * s)
            };
            g.FillPolygon(headBrush, headPts);
        }

        // Glowing Golden Eyes
        using (var eyeBrush = new SolidBrush(Color.FromArgb(255, 251, 191, 36)))
        {
            g.FillEllipse(eyeBrush, center - 14 * s, center - 52 * s, 7 * s, 10 * s);
            g.FillEllipse(eyeBrush, center + 7 * s, center - 52 * s, 7 * s, 10 * s);
        }

        // 5. Thorax (Middle Body)
        using (var thoraxBrush = new SolidBrush(Color.FromArgb(255, 5, 150, 105)))
        {
            var thoraxPts = new PointF[]
            {
                new(center, center - 34 * s),
                new(center + 25 * s, center - 14 * s),
                new(center, center + 6 * s),
                new(center - 25 * s, center - 14 * s)
            };
            g.FillPolygon(thoraxBrush, thoraxPts);
        }

        // 6. Abdomen (Striped Segments)
        (float top, float bot, float width, Color color)[] segments =
        [
            (center + 7 * s, center + 24 * s, 20 * s, Color.FromArgb(255, 16, 185, 129)),
            (center + 23 * s, center + 39 * s, 16 * s, Color.FromArgb(255, 15, 23, 42)),
            (center + 38 * s, center + 53 * s, 12 * s, Color.FromArgb(255, 52, 211, 153)),
            (center + 52 * s, center + 66 * s, 8 * s, Color.FromArgb(255, 15, 23, 42))
        ];

        foreach (var seg in segments)
        {
            using var sb = new SolidBrush(seg.color);
            var pts = new PointF[]
            {
                new(center - seg.width, seg.top),
                new(center + seg.width, seg.top),
                new(center + seg.width * 0.8f, seg.bot),
                new(center - seg.width * 0.8f, seg.bot)
            };
            g.FillPolygon(sb, pts);
        }

        // 7. Sharp Golden Stinger
        var stingerRect = new RectangleF(center - 6 * s, center + 65 * s, 12 * s, 22 * s);
        using (var stingerBrush = new LinearGradientBrush(
            stingerRect,
            Color.FromArgb(255, 251, 191, 36),
            Color.FromArgb(255, 52, 211, 153),
            LinearGradientMode.Vertical))
        {
            var stingerPts = new PointF[]
            {
                new(center - 6 * s, center + 65 * s),
                new(center + 6 * s, center + 65 * s),
                new(center, center + 85 * s)
            };
            g.FillPolygon(stingerBrush, stingerPts);
        }

        return bmp;
    }
}
