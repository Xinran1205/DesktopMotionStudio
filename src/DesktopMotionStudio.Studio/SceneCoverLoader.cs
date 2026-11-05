using System.IO;
using System.Windows.Media.Imaging;
using DesktopMotionStudio.Core.Scenes;

namespace DesktopMotionStudio.Studio;

internal static class SceneCoverLoader
{
    internal static BitmapSource? Load(SceneLibraryItem item)
    {
        if (!item.Catalog.IsAvailable || item.Presentation?.Cover is not string cover) return null;
        SceneAssetReadResult read = SceneAssetResolver.ReadFile(item.Catalog.DirectoryPath, cover, SceneLibrary.MaximumCoverBytes);
        if (read.Error != SceneAssetError.None || read.Bytes is not byte[] bytes) return null;
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            BitmapDecoder decoder;
            if (bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            else if (bytes.AsSpan().StartsWith(new byte[] { 255, 216, 255 }))
                decoder = new JpegBitmapDecoder(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            else return null;
            if (decoder.Frames.Count != 1) return null;
            BitmapFrame frame = decoder.Frames[0];
            int width = frame.PixelWidth, height = frame.PixelHeight;
            if (width is <= 0 or > 4096 || height is <= 0 or > 4096) return null;
            double scale = Math.Min(1d, 512d / Math.Max(width, height));
            stream.Position = 0;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = Math.Max(1, (int)Math.Round(width * scale));
            image.DecodePixelHeight = Math.Max(1, (int)Math.Round(height * scale));
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or FileFormatException or ArgumentException or NotSupportedException or InvalidOperationException or System.Runtime.InteropServices.COMException) { return null; }
    }
}
