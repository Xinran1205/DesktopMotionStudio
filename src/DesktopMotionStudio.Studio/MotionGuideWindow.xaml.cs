using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;

namespace DesktopMotionStudio.Studio;

internal static class MotionGuideBrief
{
    internal static string Format(string idea, string style, int duration)
    {
        string concept = string.IsNullOrWhiteSpace(idea) ? ProductText.Get("MotionGuideExample") : idea.Trim();
        string first = (duration * .28m).ToString("0.#", CultureInfo.CurrentCulture);
        string second = (duration * .74m).ToString("0.#", CultureInfo.CurrentCulture);
        return ProductText.Format("MotionGuideTemplate", concept, style, duration, first, second);
    }
}

public partial class MotionGuideWindow : Window
{
    public MotionGuideWindow()
    {
        InitializeComponent();
        Height = Math.Min(Height, Math.Max(MinHeight, SystemParameters.WorkArea.Height - 60));
        Refresh();
    }

    private void OnInput(object sender, RoutedEventArgs e)
    {
        if (IsInitialized && Brief is not null && CopyStatus is not null) Refresh();
    }

    private void Refresh()
    {
        if (StyleChoice.SelectedItem is not ComboBoxItem { Content: string style } ||
            DurationChoice.SelectedItem is not ComboBoxItem { Content: string seconds } ||
            !int.TryParse(seconds, NumberStyles.None, CultureInfo.InvariantCulture, out int duration)) return;
        Brief.Text = MotionGuideBrief.Format(Idea.Text, style, duration);
        CopyStatus.Text = "";
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        Refresh();
        try
        {
            Clipboard.SetText(Brief.Text);
            CopyStatus.Text = ProductText.Get("MotionGuideCopied");
        }
        catch (ExternalException)
        {
            CopyStatus.Text = ProductText.Get("MotionGuideClipboardBusy");
        }
    }
}
