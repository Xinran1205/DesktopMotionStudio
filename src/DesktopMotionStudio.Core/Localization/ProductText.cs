using System.Globalization;
using System.Resources;

namespace DesktopMotionStudio.Core.Localization;

public static class ProductText
{
    public const string DefaultLanguage = "zh-CN";
    public const string English = "en";

    private static readonly ResourceManager ChineseText = new(
        "DesktopMotionStudio.Core.Localization.ClientChinese", typeof(ProductText).Assembly);
    private static readonly ResourceManager EnglishText = new(
        "DesktopMotionStudio.Core.Localization.ClientEnglish", typeof(ProductText).Assembly);

    public static string Language { get; private set; } = DefaultLanguage;

    public static bool IsSupported(string? language) => language is DefaultLanguage or English;

    public static void UseLanguage(string? language) => Language = language == English ? English : DefaultLanguage;

    public static string Get(string key) => Get(key, Language);

    public static string Get(string key, string language)
    {
        ResourceManager selected = language == English ? EnglishText : ChineseText;
        return selected.GetString(key, CultureInfo.InvariantCulture)
            ?? ChineseText.GetString(key, CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException($"Missing product text resource '{key}'.");
    }

    public static string Format(string key, params object?[] values) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), values);
}
