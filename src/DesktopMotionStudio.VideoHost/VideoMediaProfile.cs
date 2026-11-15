using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.MediaFoundation;

namespace DesktopMotionStudio.VideoHost;

internal static class VideoMediaProfile
{
    internal static bool HasMp4Header(Stream asset)
    {
        Span<byte> header = stackalloc byte[16];
        if (asset.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) != header.Length ||
            !header[4..8].SequenceEqual("ftyp"u8)) return false;
        uint size = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (size is < 16 or > 4096 || size > asset.Length || size % 4 != 0) return false;
        if (IsMp4Brand(header[8..12])) return true;
        Span<byte> brand = stackalloc byte[4];
        for (int offset = 16; offset < size; offset += brand.Length)
        {
            asset.ReadExactly(brand);
            if (IsMp4Brand(brand)) return true;
        }
        return false;
    }

    private static bool IsMp4Brand(ReadOnlySpan<byte> brand) =>
        brand.SequenceEqual("isom"u8) || (brand[..3].SequenceEqual("iso"u8) && brand[3] is >= (byte)'2' and <= (byte)'9') ||
        brand.SequenceEqual("mp41"u8) || brand.SequenceEqual("mp42"u8) || brand.SequenceEqual("avc1"u8) || brand.SequenceEqual("M4V "u8);

    internal static bool HasAudio(string path)
    {
        PInvoke.MFCreateSourceReaderFromURL(path, null!, out IMFSourceReader reader).ThrowOnFailure();
        try
        {
            bool video = false, audio = false;
            for (uint stream = 0; ; stream++)
            {
                IMFMediaType type;
                try { reader.GetNativeMediaType(stream, 0, out type); }
                catch (COMException error) when (error.HResult == HRESULT.MF_E_INVALIDSTREAMNUMBER) { break; }

                try
                {
                    type.GetGUID(PInvoke.MF_MT_MAJOR_TYPE, out Guid kind);
                    type.GetGUID(PInvoke.MF_MT_SUBTYPE, out Guid codec);
                    if (kind == PInvoke.MFMediaType_Video && !video)
                    {
                        ValidateVideo(type, codec);
                        video = true;
                    }
                    else if (kind == PInvoke.MFMediaType_Audio && !audio && codec == PInvoke.MFAudioFormat_AAC) audio = true;
                    else throw new InvalidDataException("Unsupported or additional MP4 track.");
                }
                finally { Marshal.FinalReleaseComObject(type); }
            }
            if (!video) throw new InvalidDataException("The MP4 has no video track.");
            return audio;
        }
        finally { Marshal.FinalReleaseComObject(reader); }
    }

    private static void ValidateVideo(IMFMediaType video, Guid codec)
    {
        video.GetUINT64(PInvoke.MF_MT_FRAME_SIZE, out ulong size);
        video.GetUINT64(PInvoke.MF_MT_FRAME_RATE, out ulong fps);
        uint width = (uint)(size >> 32), height = (uint)size;
        uint numerator = (uint)(fps >> 32), denominator = (uint)fps;
        uint? profile = GetOptionalUInt(video, PInvoke.MF_MT_MPEG2_PROFILE);
        uint? transfer = GetOptionalUInt(video, PInvoke.MF_MT_TRANSFER_FUNCTION);
        ulong? aspect = GetOptionalRatio(video, PInvoke.MF_MT_PIXEL_ASPECT_RATIO);
        if (codec != PInvoke.MFVideoFormat_H264 || !ValidSize(width, height) ||
            numerator == 0 || denominator == 0 || numerator > 30UL * denominator ||
            profile is not (null or 66 or 77 or 100) ||
            transfer is (uint)MFVideoTransferFunction.MFVideoTransFunc_2084 or (uint)MFVideoTransferFunction.MFVideoTransFunc_HLG ||
            GetOptionalUInt(video, PInvoke.MF_MT_VIDEO_ROTATION) is not (null or 0) ||
            aspect is ulong ratio && ((uint)ratio == 0 || (ratio >> 32) != (uint)ratio))
            throw new InvalidDataException("Unsupported MP4 video profile.");
    }

    internal static bool ValidSize(uint width, uint height) =>
        width is > 0 and <= 1920 && height is > 0 and <= 1920 && Math.Min(width, height) <= 1080;

    private static uint? GetOptionalUInt(IMFMediaType type, Guid key)
    {
        try { type.GetUINT32(key, out uint value); return value; }
        catch (COMException error) when (error.HResult == HRESULT.MF_E_ATTRIBUTENOTFOUND) { return null; }
    }

    private static ulong? GetOptionalRatio(IMFMediaType type, Guid key)
    {
        try { type.GetUINT64(key, out ulong value); return value; }
        catch (COMException error) when (error.HResult == HRESULT.MF_E_ATTRIBUTENOTFOUND) { return null; }
    }
}
