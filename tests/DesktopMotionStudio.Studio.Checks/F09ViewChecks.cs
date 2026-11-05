using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopMotionStudio.Core.Scenes;
using DesktopMotionStudio.Studio;

internal static class F09ViewChecks
{
    internal static void Run(ref int failures)
    {
        string root = Path.Combine(Path.GetTempPath(), $"dms-f09-view-{Guid.NewGuid():N}"); Directory.CreateDirectory(root);
        try
        {
            string cover = Path.Combine(root, "cover.png");
            var item = new SceneLibraryItem(new("one", root, new(1, "scene-one", "1.0.0", "Fixture", SceneDisplayMode.Centered, []), SceneCatalogDiagnostic.None),
                new(1, "scene-one", "1.0.0", "", "", "", "cover.png"), SceneDataError.None, null, SceneDataError.Missing, SceneAssetError.None);
            WritePng(cover, 1024, 512);
            BitmapSource? image = SceneCoverLoader.Load(item);
            Program.Record("cover decoding is bounded frozen and independent of disposed streams", image is { IsFrozen: true, PixelWidth: 512, PixelHeight: 256 }, ref failures);
            using (File.Open(cover, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            byte[] pixel = new byte[4]; image?.CopyPixels(new System.Windows.Int32Rect(0, 0, 1, 1), pixel, 4, 0);
            Program.Record("decoded cover releases its file and retains real pixels", pixel[0] == 80 && pixel[1] == 120 && pixel[2] == 210, ref failures);
            var jpeg = new JpegBitmapEncoder(); jpeg.Frames.Add(BitmapFrame.Create(image!));
            using (var output = File.Create(Path.Combine(root, "cover.jpg"))) jpeg.Save(output);
            Program.Record("valid JPEG follows the same bounded frozen cover path", SceneCoverLoader.Load(item with { Presentation = item.Presentation! with { Cover = "cover.jpg" } }) is { IsFrozen: true, PixelWidth: <= 512, PixelHeight: <= 512 }, ref failures);
            WritePng(cover, 1, 4096);
            image = SceneCoverLoader.Load(item);
            Program.Record("portrait cover also bounds decoded height", image is { PixelWidth: <= 512, PixelHeight: <= 512 }, ref failures);
            WritePng(cover, 4097, 1);
            Program.Record("oversized source dimensions fall back before full decode", SceneCoverLoader.Load(item) is null, ref failures);
            File.WriteAllBytes(cover, new byte[SceneLibrary.MaximumCoverBytes + 1]);
            Program.Record("oversized cover bytes fall back", SceneCoverLoader.Load(item) is null, ref failures);
            File.WriteAllText(cover, "corrupt image");
            Program.Record("corrupt optional cover falls back", SceneCoverLoader.Load(item) is null, ref failures);
            File.WriteAllBytes(cover, [137, 80, 78, 71, 13, 10, 26, 10]);
            Program.Record("invalid signed PNG remains a nonfatal cover fallback", SafeFallback(item), ref failures);
            File.WriteAllBytes(cover, [255, 216, 255, 217]);
            Program.Record("invalid signed JPEG remains a nonfatal cover fallback", SafeFallback(item), ref failures);
            File.Delete(cover);
            Program.Record("missing optional cover falls back", SceneCoverLoader.Load(item) is null, ref failures);
            Program.Record("escaping cover is rejected", SceneCoverLoader.Load(item with { Presentation = item.Presentation! with { Cover = "../outside.png" } }) is null, ref failures);
        }
        finally { Directory.Delete(root, true); }
    }

    internal static void WritePng(string path, int width, int height)
    {
        byte[] pixels = new byte[checked(width * height * 4)];
        for (int index = 0; index < pixels.Length; index += 4) { pixels[index] = 80; pixels[index + 1] = 120; pixels[index + 2] = 210; pixels[index + 3] = 255; }
        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = File.Create(path); encoder.Save(stream);
    }

    private static bool SafeFallback(SceneLibraryItem item)
    {
        try { return SceneCoverLoader.Load(item) is null; }
        catch (Exception) { return false; }
    }
}
