using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

if (args.Length != 2 || !File.Exists(args[0])) return 2;
string output = Path.GetFullPath(args[1]);
if (Directory.Exists(output) || File.Exists(output))
{
    Console.Error.WriteLine("Choose a new directory for this render.");
    return 2;
}
string stage = output + ".render-" + Guid.NewGuid().ToString("N");
Directory.CreateDirectory(stage);
const int width = 1920, height = 1080, frameCount = 180;
var encode = new ProcessStartInfo(Path.GetFullPath(args[0])) { UseShellExecute = false, RedirectStandardInput = true };
foreach (string value in new[]
{
    "-n", "-hide_banner", "-loglevel", "error", "-f", "rawvideo", "-pixel_format", "bgr24", "-video_size", "1920x1080",
    "-framerate", "30", "-i", "pipe:0", "-an", "-c:v", "libx264", "-preset", "medium", "-crf", "18",
    "-pix_fmt", "yuv420p", "-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709",
    "-movflags", "+faststart", Path.Combine(stage, "orbit-ignition.mp4")
}) encode.ArgumentList.Add(value);
using var encoder = Process.Start(encode) ?? throw new IOException("Could not start the encoder.");
using var surface = new Bitmap(width, height, PixelFormat.Format24bppRgb);
byte[] pixels = new byte[width * height * 3];
var bounds = new Rectangle(0, 0, width, height);
using (var target = encoder.StandardInput.BaseStream)
{
    for (int frame = 0; frame < frameCount; frame++)
    {
        using (var graphics = Graphics.FromImage(surface)) Orbit.Draw(graphics, frame / 30f);
        if (frame == 82) surface.Save(Path.Combine(stage, "cover.png"), ImageFormat.Png);
        BitmapData bitmap = surface.LockBits(bounds, ImageLockMode.ReadOnly, surface.PixelFormat);
        try
        {
            if (bitmap.Stride != width * 3) throw new IOException("Unexpected frame stride.");
            Marshal.Copy(bitmap.Scan0, pixels, 0, pixels.Length);
            target.Write(pixels);
        }
        finally { surface.UnlockBits(bitmap); }
    }
}
encoder.WaitForExit();
if (encoder.ExitCode == 0) Directory.Move(stage, output);
return encoder.ExitCode;

internal static class Orbit
{
    private static readonly PointF[] Stars = CreateStars();

    internal static void Draw(Graphics canvas, float time)
    {
        canvas.SmoothingMode = SmoothingMode.AntiAlias;
        canvas.ScaleTransform(2, 2);
        using var sky = new LinearGradientBrush(new Rectangle(0, 0, 960, 540), Color.FromArgb(5, 10, 22), Color.FromArgb(10, 27, 42), 90);
        canvas.FillRectangle(sky, 0, 0, 960, 540);
        float rise = Ease(1.8f, 4.6f, time), escape = Ease(4.15f, 5.75f, time);
        float ignition = Ease(1.55f, 2.05f, time);
        float gate = 200 + 100 * rise;

        for (int i = 0; i < Stars.Length; i++)
        {
            PointF star = Stars[i];
            float alpha = .45f + .4f * MathF.Sin(i * 2.19f + time * 1.7f);
            using var light = new SolidBrush(Color.FromArgb((int)(145 * alpha), 164, 208, 235));
            canvas.FillEllipse(light, star.X, (star.Y + 60 * rise) % 410, i % 9 == 0 ? 2 : 1, i % 9 == 0 ? 2 : 1);
        }

        Glow(canvas, 480, gate, 310 + 70 * rise, 140, Color.FromArgb(34, 70, 219, 255));
        float shock = Ease(1.71f, 2.43f, time);
        if (shock is > 0 and < 1)
        {
            float size = 30 + 480 * shock;
            using var wave = new Pen(Color.FromArgb((int)(180 * (1 - shock)), 143, 231, 255), 2);
            canvas.DrawEllipse(wave, 480 - size / 2, 344 - size * .14f, size, size * .28f);
        }
        for (int ring = 0; ring < 4; ring++)
        {
            float radius = 103 + ring * 34 + 21 * rise;
            var ellipse = new RectangleF(480 - radius, gate - radius * .62f, 2 * radius, 1.24f * radius);
            using var line = new Pen(Color.FromArgb(105 - ring * 15, 73, 174, 209), ring == 0 ? 1.8f : 1);
            canvas.DrawEllipse(line, ellipse);
            using var segment = new Pen(Color.FromArgb(130 - ring * 17, 106, 231, 253), 2.5f);
            for (int side = 0; side < 4; side++)
                canvas.DrawArc(segment, ellipse, 90 * side + 9 * ring + time * (ring % 2 == 0 ? 12 : -15), 18 + ring * 3);
        }

        Hangar(canvas, gate, rise, time);
        float y = 310 - 150 * rise - 18 * escape;
        float scale = (1 - .20f * rise) * (1 - .91f * escape);
        float thrust = ignition * (1 - .82f * escape);
        if (thrust > 0)
        {
            float flicker = .92f + .08f * MathF.Sin(45 * time);
            Glow(canvas, 480, y + 33 * scale, 130 * thrust + 18, 54 * thrust + 10, Color.FromArgb((int)(104 * thrust), 247, 125, 47));
            using var tail = new LinearGradientBrush(new RectangleF(0, y + 13 * scale, 1, 15 + 184 * thrust), Color.FromArgb(253, 255, 239, 191), Color.FromArgb(0, 251, 102, 46), 90);
            float baseWidth = 5 + 17 * thrust * scale;
            PointF[] flame = [new(480 - baseWidth, y + 20 * scale), new(480 + baseWidth, y + 20 * scale),
                new(483, y + 24 * scale + 160 * thrust * flicker), new(479, y + 42 * scale + 179 * thrust)];
            canvas.FillPolygon(tail, flame);
            for (int i = 0; i < 42; i++)
            {
                float phase = (time * (.8f + i % 7 * .11f) + i * .143f) % 1;
                float offset = (i % 2 == 0 ? -1 : 1) * (4 + i % 12 * 2) * phase * thrust;
                using var spark = new Pen(Color.FromArgb((int)(155 * (1 - phase) * thrust), 255, 168, 88), 1);
                float sy = y + 28 * scale + phase * 175 * thrust;
                canvas.DrawLine(spark, 480 + offset, sy, 480 + offset * 1.06f, sy + 3 + 12 * phase);
            }
        }

        Vehicle(canvas, y, scale);
        if (escape > .6f)
        {
            float pulse = MathF.Sin(Ease(4.9f, 5.85f, time) * MathF.PI);
            Glow(canvas, 480, y - 35 * scale, 45 * pulse + 3, 18 * pulse + 3, Color.FromArgb((int)(145 * pulse), 148, 241, 255));
        }
    }

    private static void Hangar(Graphics canvas, float gate, float rise, float time)
    {
        using var metal = new LinearGradientBrush(new Rectangle(0, 0, 960, 540), Color.FromArgb(21, 44, 62), Color.FromArgb(6, 11, 19), 78);
        using var edge = new Pen(Color.FromArgb(112, 61, 121, 145), 1.3f);
        using var strip = new Pen(Color.FromArgb(148, 76, 217, 243), 2);
        for (int side = 0; side < 2; side++)
        {
            GraphicsState state = canvas.Save();
            if (side == 1) { canvas.TranslateTransform(960, 0); canvas.ScaleTransform(-1, 1); }
            float offset = 120 * rise;
            PointF[] wall = [new(0, 0), new(181-offset, 0), new(318-offset, gate+25), new(257-offset, gate+92), new(125-offset, 540), new(0, 540)];
            canvas.FillPolygon(metal, wall);
            canvas.DrawLines(edge, wall);
            using var panel = new SolidBrush(Color.FromArgb(90, 8, 16, 27));
            canvas.FillPolygon(panel, [new PointF(0, 0), new PointF(126 - offset, 0), new PointF(247 - offset, gate + 25),
                new PointF(204 - offset, gate + 55), new PointF(84 - offset * .6f, 540), new PointF(0, 540)]);
            using var seam = new Pen(Color.FromArgb(102, 52, 101, 126), 1);
            for (int rail = 0; rail < 3; rail++)
            {
                float x = 42 + 24 * rail - offset * .52f;
                canvas.DrawLines(seam, [new PointF(x, 0), new PointF(x + 116, gate + 20),
                    new PointF(x + 70, gate + 90), new PointF(x - 32, 540)]);
            }
            for (int i = 0; i < 7; i++)
            {
                float shift = (i * 31 + time * (8 + rise * 26)) % 217;
                float sy = gate + 100 + shift;
                float sx = 241 - offset - shift * .43f;
                canvas.DrawLine(edge, 0, sy + 41, sx, sy);
                canvas.DrawLine(strip, sx - 20, sy + 3, sx, sy);
            }
            for (int light = 0; light < 18; light++)
            {
                float sy = 48 + 19 * light;
                float sx = 43 + sy * .23f - offset * .45f;
                int alpha = (int)(75 + 75 * MathF.Pow(.5f + .5f * MathF.Sin(time * 5 - light * .51f), 4));
                using var dot = new SolidBrush(Color.FromArgb(alpha, 97, 201, 231));
                canvas.FillRectangle(dot, sx, sy, 12, 1.5f);
            }
            canvas.Restore(state);
        }
    }

    private static void Vehicle(Graphics canvas, float y, float scale)
    {
        GraphicsState state = canvas.Save();
        canvas.TranslateTransform(480, y);
        canvas.ScaleTransform(scale, scale);
        using var shell = new LinearGradientBrush(new Rectangle(-42, -80, 84, 130), Color.FromArgb(53, 84, 107), Color.FromArgb(181, 210, 221), 0f);
        using var line = new Pen(Color.FromArgb(190, 112, 224, 252), 1.5f);
        PointF[] hull = [new(0,-78), new(13,-51), new(19,-19), new(36,15), new(38,33),
            new(14,27), new(9,35), new(-9,35), new(-14,27), new(-38,33), new(-36,15), new(-19,-19), new(-13,-51)];
        canvas.FillPolygon(shell, hull);
        canvas.DrawPolygon(line, hull);
        using var facet = new SolidBrush(Color.FromArgb(183, 17, 39, 58));
        canvas.FillPolygon(facet, [new(0,-70), new(1,30), new(-13,24), new(-16,-16), new(-10,-49)]);
        using var glass = new SolidBrush(Color.FromArgb(213, 94, 212, 234));
        canvas.FillPolygon(glass, [new(0,-49), new(6,-32), new(3,-18), new(-4,-18), new(-6,-32)]);
        canvas.DrawLine(line, 8, -8, 12, 21);
        canvas.DrawLine(line, -8, -8, -12, 21);
        using var vents = new Pen(Color.FromArgb(165, 26, 53, 71), 1);
        for (int slot = 0; slot < 4; slot++)
        {
            int position = 7 + 5 * slot;
            canvas.DrawLine(vents, 15, position, 23 + slot * 2, position + 4);
            canvas.DrawLine(vents, -15, position, -23 - slot * 2, position + 4);
        }
        canvas.Restore(state);
    }

    private static void Glow(Graphics canvas, float x, float y, float width, float height, Color color)
    {
        using var path = new GraphicsPath();
        path.AddEllipse(x - width / 2, y - height / 2, width, height);
        using var brush = new PathGradientBrush(path) { CenterColor = color, SurroundColors = [Color.FromArgb(0, color)] };
        canvas.FillPath(brush, path);
    }

    private static float Ease(float start, float end, float time)
    {
        float value = Math.Clamp((time - start) / (end - start), 0, 1);
        return value * value * (3 - 2 * value);
    }

    private static PointF[] CreateStars()
    {
        var random = new Random(7142);
        return Enumerable.Range(0, 185).Select(_ => new PointF(random.Next(960), random.Next(410))).ToArray();
    }
}
