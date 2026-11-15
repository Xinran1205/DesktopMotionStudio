using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace DesktopMotionStudio.Studio;

public partial class MotionDirectorWindow : Window
{
    public MotionDirectorWindow()
    {
        InitializeComponent();
        Height = Math.Min(Height, Math.Max(MinHeight, SystemParameters.WorkArea.Height - 60));
        Loaded += (_, _) => ApplicationName.Focus();
    }

    private void OnInputChanged(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized) return;
        bool hadResult = Instruction.Text.Length > 0;
        Instruction.Clear();
        CopyButton.IsEnabled = SaveButton.IsEnabled = false;
        Status.Text = hadResult ? ProductText.Get("MotionDirectorRegenerate") : "";
    }

    private void OnGenerate(object sender, RoutedEventArgs e)
    {
        string? error = MotionDirectorPrompt.Generate(ApplicationName.Text, Idea.Text, ProductText.Language, out string? result);
        Instruction.Text = result ?? "";
        CopyButton.IsEnabled = SaveButton.IsEnabled = result is not null;
        Status.Text = ProductText.Get(error ?? "MotionDirectorGenerated");
        if (error == "MotionDirectorNameRequired") ApplicationName.Focus();
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(Instruction.Text);
            Status.Text = ProductText.Get("MotionDirectorCopied");
        }
        catch (ExternalException)
        {
            Status.Text = ProductText.Get("MotionDirectorClipboardBusy");
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = ProductText.Get("MotionDirectorSave"),
            Filter = ProductText.Get("MotionDirectorMarkdownFilter"),
            DefaultExt = ".md",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = MotionDirectorPrompt.SuggestedFileName(ApplicationName.Text)
        };
        try
        {
            if (dialog.ShowDialog(this) != true) return;
            File.WriteAllText(dialog.FileName, Instruction.Text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Status.Text = ProductText.Get("MotionDirectorSaved");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ExternalException)
        {
            Status.Text = ProductText.Get("MotionDirectorSaveFailed");
        }
    }
}
