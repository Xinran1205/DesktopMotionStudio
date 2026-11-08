using System.Drawing.Drawing2D;

namespace DesktopMotionStudio.MotionHost;

internal static class TechFramePainter
{
    internal static RectangleF Composition(Rectangle bounds)
    {
        float width = Math.Min(bounds.Width * .90f, bounds.Height * 1.6f);
        float height = width * 9 / 16;
        return new RectangleF(bounds.Left + (bounds.Width - width) / 2, bounds.Top + (bounds.Height - height) / 2, width, height);
    }

    internal static void Paint(Graphics graphics, Rectangle bounds, double progress, Color accent, bool reducedMotion)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var background = new LinearGradientBrush(bounds, Color.FromArgb(9, 13, 20), Color.FromArgb(23, 29, 39), 65);
        graphics.FillRectangle(background, bounds);

        RectangleF composition = Composition(bounds);
        if (composition.Width < 2) return;
        GraphicsState state = graphics.Save();
        try
        {
            graphics.TranslateTransform(composition.Left, composition.Top);
            graphics.ScaleTransform(composition.Width / 800, composition.Height / 450);
            PaintComposition(graphics, reducedMotion ? .53f : (float)Math.Clamp(progress, 0, 1), accent, reducedMotion);
        }
        finally { graphics.Restore(state); }
    }

    private static void PaintComposition(Graphics graphics, float progress, Color accent, bool reducedMotion)
    {
        float ignite = Ease(progress, 0, .175f);
        float align = Ease(progress, .175f, .60f);
        float resolve = Ease(progress, .60f, 1);
        float visibility = ignite * (1 - resolve * .88f);
        var ambient = new RectangleF(60, 30, 680, 390);
        using var path = new GraphicsPath();
        path.AddEllipse(ambient);
        using var glow = new PathGradientBrush(path)
        {
            CenterColor = Tint(accent, 46 * visibility),
            SurroundColors = [Color.Transparent]
        };
        graphics.FillEllipse(glow, ambient);

        using var grid = new Pen(Tint(accent, 19 * visibility));
        for (int x = 80; x <= 720; x += 32) graphics.DrawLine(grid, x, 62, x, 388);
        for (int y = 66; y <= 386; y += 32) graphics.DrawLine(grid, 80, y, 720, y);

        float inset = resolve * 32;
        RectangleF front = new(151 + inset, 100 + inset * .35f, 498 - inset * 2, 250 - inset * .70f);
        float drift = (1 - align) * 22;
        RectangleF rear = front;
        rear.Offset(-18 - drift, 13 + drift * .5f);
        using var rearFill = new SolidBrush(Tint(accent, 12 * visibility));
        using var rearEdge = new Pen(Tint(accent, 94 * visibility), 1.25f);
        graphics.FillRectangle(rearFill, rear);
        graphics.DrawRectangle(rearEdge, rear.X, rear.Y, rear.Width, rear.Height);

        using var frontFill = new LinearGradientBrush(front, Tint(accent, 28 * visibility), Color.FromArgb(190, 11, 18, 27), 90);
        using var edge = new Pen(Tint(accent, 160 * visibility), 1.6f);
        graphics.FillRectangle(frontFill, front);
        graphics.DrawRectangle(edge, front.X, front.Y, front.Width, front.Height);
        graphics.DrawLine(rearEdge, front.Left, front.Top + 26, front.Right, front.Top + 26);
        using var pale = new Pen(Tint(Color.FromArgb(221, 248, 255), (140 + 105 * align) * visibility), 2.7f);
        DrawCorner(graphics, pale, front.Left, front.Top, 1, 1);
        DrawCorner(graphics, pale, front.Right, front.Top, -1, 1);
        DrawCorner(graphics, pale, front.Left, front.Bottom, 1, -1);
        DrawCorner(graphics, pale, front.Right, front.Bottom, -1, -1);

        using var reticle = new Pen(Tint(accent, 120 * visibility));
        graphics.DrawLine(reticle, 381, 225, 394, 225);
        graphics.DrawLine(reticle, 406, 225, 419, 225);
        graphics.DrawLine(reticle, 400, 206, 400, 219);
        graphics.DrawLine(reticle, 400, 231, 400, 244);
        if (!reducedMotion && progress < .35f)
        {
            float x = front.Left + front.Width * Ease(progress, 0, .35f);
            float power = MathF.Sin(MathF.PI * (progress / .35f));
            using var bloom = new Pen(Tint(accent, 36 * power), 12);
            using var beam = new Pen(Tint(Color.FromArgb(218, 248, 255), 230 * power), 1.6f);
            graphics.DrawLine(bloom, x, front.Top + 9, x, front.Bottom - 9);
            graphics.DrawLine(beam, x, front.Top + 9, x, front.Bottom - 9);
        }
    }

    private static void DrawCorner(Graphics graphics, Pen pen, float x, float y, int horizontal, int vertical)
    {
        graphics.DrawLines(pen, [new PointF(x, y + vertical * 14), new PointF(x, y), new PointF(x + horizontal * 24, y)]);
    }

    private static Color Tint(Color color, float alpha) => Color.FromArgb(Math.Clamp((int)alpha, 0, 255), color);

    private static float Ease(float value, float from, float to)
    {
        float t = Math.Clamp((value - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
