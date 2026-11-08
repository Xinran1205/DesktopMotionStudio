using System.Drawing.Drawing2D;
using DesktopMotionStudio.Core.Scenes;

namespace DesktopMotionStudio.MotionHost;

internal sealed class SceneSurface : Control
{
    private readonly SceneTimeline _timeline;
    private readonly bool _reducedMotion;
    private readonly Color _accent;
    private SceneFrame _frame;
    private double _progress;
    internal event EventHandler? RenderFailed;

    internal SceneSurface(ScenePlaybackDefinition definition, bool reducedMotion)
    {
        _timeline = new SceneTimeline(definition);
        _reducedMotion = reducedMotion;
        _accent = ColorTranslator.FromHtml(definition.Accent);
        _frame = _timeline.Evaluate(TimeSpan.Zero, reducedMotion);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AccessibleName = "场景画面";
        TabStop = false;
    }

    internal void Present(TimeSpan elapsed)
    {
        if (_timeline.Definition.IsTechFrame)
            _progress = elapsed.TotalMilliseconds / _timeline.Definition.DurationMilliseconds;
        else
            _frame = _timeline.Evaluate(elapsed, _reducedMotion);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        try
        {
            if (_timeline.Definition.IsTechFrame)
                TechFramePainter.Paint(e.Graphics, ClientRectangle, _progress, _accent, _reducedMotion);
            else
                PaintScene(e.Graphics, ClientRectangle);
        }
        catch (Exception) { RenderFailed?.Invoke(this, EventArgs.Empty); }
    }

    private void PaintScene(Graphics graphics, Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var background = new LinearGradientBrush(bounds, Color.FromArgb(13, 22, 43), Color.FromArgb(25, 45, 79), 35);
        graphics.FillRectangle(background, bounds);
        float size = Math.Min(bounds.Width, bounds.Height);
        float centerX = bounds.Left + bounds.Width / 2f, centerY = bounds.Top + bounds.Height / 2f;
        float radius = size * .28f * (float)_frame.Scale;
        int Alpha(double amount) => Math.Clamp((int)Math.Round(amount * _frame.Opacity), 0, 255);
        float unit = Math.Max(.25f, size / 400f);
        for (int i = 12; i > 0; i--)
        {
            using var glow = new Pen(Color.FromArgb(Alpha(7), _accent), 5 * unit);
            float outer = radius + i * 3 * unit;
            graphics.DrawEllipse(glow, centerX - outer, centerY - outer, outer * 2, outer * 2);
        }
        using var ring = new Pen(Color.FromArgb(Alpha(230), _accent), 3 * unit);
        graphics.DrawEllipse(ring, centerX - radius, centerY - radius, radius * 2, radius * 2);
        for (int i = 0; i < 3; i++)
        {
            double angle = (_frame.RotationDegrees + i * 120) * Math.PI / 180;
            float x = centerX + radius * (float)Math.Cos(angle), y = centerY + radius * (float)Math.Sin(angle);
            using var dot = new SolidBrush(Color.FromArgb(Alpha(255), _accent));
            graphics.FillEllipse(dot, x - 7 * unit, y - 7 * unit, 14 * unit, 14 * unit);
        }
        using var font = new Font("Segoe UI", Math.Max(1, size * .16f), FontStyle.Bold, GraphicsUnit.Pixel);
        using var text = new SolidBrush(Color.FromArgb(Alpha(255), 235, 241, 255));
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString(_timeline.Definition.Glyph, font, text, new RectangleF(centerX - size / 4, centerY - size / 4, size / 2, size / 2), format);
    }
}
