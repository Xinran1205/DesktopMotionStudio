using System.Drawing;
using System.Windows.Forms;

namespace DesktopMotionStudio.WindowHost.Poc;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (!TryParse(args, out var mode, out var monitorIndex))
        {
            Console.Error.WriteLine("Usage: WindowHost.Poc [--mode full|centered] [--monitor <zero-based index>]");
            return 2;
        }

        ApplicationConfiguration.Initialize();

        Screen[] screens = Screen.AllScreens;
        if (screens.Length == 0)
        {
            Console.Error.WriteLine("No interactive display is available.");
            return 3;
        }

        Screen screen;
        if (monitorIndex >= screens.Length)
        {
            Console.Error.WriteLine($"Monitor {monitorIndex} not found; using primary display.");
            screen = Screen.PrimaryScreen ?? screens[0];
        }
        else
        {
            screen = screens[monitorIndex];
        }
        using var window = new PrototypeWindow(mode, screen.WorkingArea);
        Application.Run(window);
        return 0;
    }

    private static bool TryParse(string[] args, out DisplayMode mode, out int monitorIndex)
    {
        mode = DisplayMode.Centered;
        monitorIndex = 0;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--mode" && ++i < args.Length)
            {
                mode = args[i] switch
                {
                    "full" => DisplayMode.Full,
                    "centered" => DisplayMode.Centered,
                    _ => DisplayMode.Invalid
                };
                if (mode == DisplayMode.Invalid) return false;
            }
            else if (args[i] == "--monitor" && ++i < args.Length &&
                     int.TryParse(args[i], out monitorIndex) && monitorIndex >= 0)
            {
                // Parsed successfully.
            }
            else
            {
                return false;
            }
        }
        return true;
    }
}

internal enum DisplayMode { Invalid, Full, Centered }

internal sealed class PrototypeWindow : Form
{
    private static readonly Color TransparentColor = Color.Magenta;
    private readonly Panel _card;
    public PrototypeWindow(DisplayMode mode, Rectangle workArea)
    {
        Text = "Desktop Motion Studio Window Host POC";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = true;
        TopMost = true;
        KeyPreview = true;
        BackColor = TransparentColor;
        TransparencyKey = TransparentColor;
        StartPosition = FormStartPosition.Manual;
        Bounds = mode == DisplayMode.Full
            ? workArea
            : CenteredBounds(workArea);
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };

        _card = new Panel
        {
            BackColor = Color.FromArgb(22, 30, 50),
            BorderStyle = BorderStyle.FixedSingle
        };
        Controls.Add(_card);

        var title = new Label
        {
            Text = "Motion Studio",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(26, 34)
        };
        _card.Controls.Add(title);

        var hint = new Label
        {
            Text = $"Window host feasibility · {mode}\nPress Esc or choose Close",
            ForeColor = Color.LightSteelBlue,
            Font = new Font("Segoe UI", 11),
            AutoSize = true,
            Location = new Point(28, 100)
        };
        _card.Controls.Add(hint);

        var close = new Button
        {
            Text = "Close",
            Size = new Size(90, 34),
            Location = new Point(28, 175),
            TabIndex = 0
        };
        close.Click += (_, _) => Close();
        _card.Controls.Add(close);
        ActiveControl = close;
        Shown += (_, _) => LayoutCard();
        Resize += (_, _) => LayoutCard();
    }

    private void LayoutCard()
    {
        _card.Size = new Size(Math.Min(520, ClientSize.Width), Math.Min(260, ClientSize.Height));
        _card.Location = new Point(
            (ClientSize.Width - _card.Width) / 2,
            (ClientSize.Height - _card.Height) / 2);
    }

    private static Rectangle CenteredBounds(Rectangle workArea)
    {
        int width = Math.Min(720, workArea.Width);
        int height = Math.Min(420, workArea.Height);
        return new Rectangle(
            workArea.Left + (workArea.Width - width) / 2,
            workArea.Top + (workArea.Height - height) / 2,
            width, height);
    }
}
