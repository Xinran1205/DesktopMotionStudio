global using DesktopMotionStudio.Core.Localization;
using System.Windows.Markup;

namespace DesktopMotionStudio.Studio;

public sealed class TextExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => ProductText.Get(key);
}
