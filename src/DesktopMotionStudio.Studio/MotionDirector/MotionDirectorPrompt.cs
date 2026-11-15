using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DesktopMotionStudio.Studio;

internal static class MotionDirectorPrompt
{
    internal const int MaxAppNameLength = 100;
    internal const int MaxIdeaLength = 1200;
    private const string DeferredIdea = "MotionDirectorDeferredIdea";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions InputFormat = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    internal static string? Generate(string appName, string idea, string language, out string? instruction)
    {
        instruction = null;
        appName = appName.Trim();
        idea = idea.Trim();
        if (appName.Length == 0) return "MotionDirectorNameRequired";
        if (appName.Length > MaxAppNameLength) return "MotionDirectorNameTooLong";
        if (idea.Length > MaxIdeaLength) return "MotionDirectorIdeaTooLong";
        if (!IsValidText(appName, multiline: false) || !IsValidText(idea, multiline: true))
            return "MotionDirectorInvalidText";

        var fields = new
        {
            appName,
            userIdea = idea.Length == 0 ? ProductText.Get(DeferredIdea, language) : idea
        };
        string templateLanguage = language == ProductText.English ? ProductText.English : ProductText.DefaultLanguage;
        using Stream resource = typeof(MotionDirectorPrompt).Assembly.GetManifestResourceStream(
            $"MotionDirector.MetaPrompt.{templateLanguage}.md") ??
            throw new InvalidOperationException("The motion director instruction is missing.");
        using var reader = new StreamReader(resource, StrictUtf8);
        string template = reader.ReadToEnd().TrimEnd().ReplaceLineEndings(Environment.NewLine);
        string input = JsonSerializer.Serialize(fields, InputFormat).ReplaceLineEndings(Environment.NewLine);
        instruction = string.Join(Environment.NewLine, template, "", "```json", input, "```", "");
        return null;
    }

    internal static string SuggestedFileName(string appName)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string readable = string.Concat(appName.Trim().Select(c => invalid.Contains(c) || char.IsControl(c) ? '-' : c));
        readable = readable.Trim(' ', '.', '-');
        return readable.Length == 0 ? "motion-director.md" : $"motion-director-{readable}.md";
    }

    private static bool IsValidText(string value, bool multiline)
    {
        if (value.Any(c => char.IsControl(c) && !(multiline && c is '\r' or '\n' or '\t') ||
            !multiline && c is '\u2028' or '\u2029')) return false;
        try { _ = StrictUtf8.GetByteCount(value); return true; }
        catch (EncoderFallbackException) { return false; }
    }
}
