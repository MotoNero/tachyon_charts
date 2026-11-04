using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Export.Video;

/// <summary>
/// Writes H.264 in MP4 with Media Foundation, which every Windows desktop brings along. The interfaces and identifiers
/// follow mfobjects.h, mfreadwrite.h and mfapi.h of the Windows SDK 10.0.26100; methods that are never called keep their
/// place in the table as placeholders.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class MediaFoundationEncoder : IVideoEncoder
{
    private const uint Version = 0x00020070;
    private const int Progressive = 2;
    private const long TicksPerSecond = 10_000_000;

    private static readonly Guid _majorType = new(0x48eba18e, 0xf8c9, 0x4687, 0xbf, 0x11, 0x0a, 0x74, 0xc9, 0xf9, 0x6a, 0x8f);
    private static readonly Guid _subtype = new(0xf7e34c9a, 0x42e8, 0x4714, 0xb7, 0x4b, 0xcb, 0x29, 0xd7, 0x2c, 0x35, 0xe5);
    private static readonly Guid _bitRate = new(0x20332624, 0xfb0d, 0x4d9e, 0xbd, 0x0d, 0xcb, 0xf6, 0x78, 0x6c, 0x10, 0x2e);
    private static readonly Guid _interlace = new(0xe2724bb8, 0xe676, 0x4806, 0xb4, 0xb2, 0xa8, 0xd6, 0xef, 0xb4, 0x4c, 0xcd);
    private static readonly Guid _frameSize = new(0x1652c33d, 0xd6b2, 0x4012, 0xb8, 0x34, 0x72, 0x03, 0x08, 0x49, 0xa3, 0x7d);
    private static readonly Guid _frameRate = new(0xc459a2e8, 0x3d2c, 0x4e44, 0xb1, 0x32, 0xfe, 0xe5, 0x15, 0x6c, 0x7b, 0xb0);
    private static readonly Guid _pixelAspect = new(0xc6376a1e, 0x8d0a, 0x4027, 0xbe, 0x45, 0x6d, 0x9a, 0x0a, 0xd3, 0x9b, 0xb6);
    private static readonly Guid _defaultStride = new(0x644b4e48, 0x1e02, 0x4516, 0xb0, 0xeb, 0xc0, 0x1c, 0xa9, 0xd4, 0x9a, 0xc6);
    private static readonly Guid _video = new(0x73646976, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71);
    private static readonly Guid _h264 = new(0x34363248, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71);
    private static readonly Guid _rgb32 = new(0x00000016, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71);

    private readonly IMFSinkWriter _writer;
    private readonly int _stream;
    private readonly PixelSize _size;
    private readonly long _frameTicks;
    private readonly byte[] _row;
    private long _frame;
    private bool _isFinished;

    public MediaFoundationEncoder(string path, PixelSize size, int frameRate, int bitRate)
    {
        (_size, _frameTicks) = (size, TicksPerSecond / frameRate);
        _row = new byte[size.Width * 4];
        Check(MFStartup(Version, 0));
        Check(MFCreateSinkWriterFromURL(path, IntPtr.Zero, IntPtr.Zero, out _writer));
        var output = CreateType(_h264, size, frameRate);
        Check(output.SetUINT32(_bitRate, bitRate));
        Check(_writer.AddStream(output, out _stream));
        var input = CreateType(_rgb32, size, frameRate);

        // A positive stride means top row first, as the frames come.
        Check(input.SetUINT32(_defaultStride, size.Width * 4));
        Check(_writer.SetInputMediaType(_stream, input, IntPtr.Zero));
        Check(_writer.BeginWriting());
        Marshal.ReleaseComObject(output);
        Marshal.ReleaseComObject(input);
    }

    public void Write(ReadOnlySpan<Rgba8> pixels)
    {
        var length = checked((int)(_size.PixelCount * 4));
        Check(MFCreateMemoryBuffer(length, out var buffer));
        Check(buffer.Lock(out var data, out _, out _));
        try
        {
            // Media Foundation keeps 32 bit RGB as blue, green, red, unused.
            for (var y = 0; y < _size.Height; y++)
            {
                var row = pixels.Slice(y * _size.Width, _size.Width);
                for (var x = 0; x < row.Length; x++)
                {
                    (_row[x * 4], _row[(x * 4) + 1], _row[(x * 4) + 2], _row[(x * 4) + 3]) = (row[x].B, row[x].G, row[x].R, 255);
                }

                Marshal.Copy(_row, 0, data + (y * _row.Length), _row.Length);
            }
        }
        finally
        {
            Check(buffer.Unlock());
        }

        Check(buffer.SetCurrentLength(length));
        Check(MFCreateSample(out var sample));
        Check(sample.AddBuffer(buffer));
        Check(sample.SetSampleTime(_frame * _frameTicks));
        Check(sample.SetSampleDuration(_frameTicks));
        Check(_writer.WriteSample(_stream, sample));
        _frame++;
        Marshal.ReleaseComObject(sample);
        Marshal.ReleaseComObject(buffer);
    }

    public void Finish()
    {
        if (!_isFinished)
        {
            _isFinished = true;
            Check(_writer.Finalize());
        }
    }

    public void Dispose()
    {
        Marshal.ReleaseComObject(_writer);
        _ = MFShutdown();
    }

    private static IMFMediaType CreateType(Guid subtype, PixelSize size, int frameRate)
    {
        Check(MFCreateMediaType(out var type));
        Check(type.SetGUID(_majorType, _video));
        Check(type.SetGUID(_subtype, subtype));
        Check(type.SetUINT32(_interlace, Progressive));
        Check(type.SetUINT64(_frameSize, ((long)size.Width << 32) | (uint)size.Height));
        Check(type.SetUINT64(_frameRate, ((long)frameRate << 32) | 1));
        Check(type.SetUINT64(_pixelAspect, (1L << 32) | 1));
        return type;
    }

    private static void Check(int result)
    {
        if (result < 0)
        {
            throw new InvalidOperationException($"Media Foundation failed with 0x{result:X8} while writing the video.");
        }
    }

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFStartup(uint version, int flags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFShutdown();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateMediaType(out IMFMediaType type);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateSample(out IMFSample sample);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateMemoryBuffer(int maxLength, out IMFMediaBuffer buffer);

    [DllImport("mfreadwrite.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int MFCreateSinkWriterFromURL(string url, IntPtr byteStream, IntPtr attributes, out IMFSinkWriter writer);

    // The 30 methods of IMFAttributes come first in each of the attribute interfaces below.
    [ComImport]
    [Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFMediaType
    {
        void GetItem();
        void GetItemType();
        void CompareItem();
        void Compare();
        void GetUINT32();
        void GetUINT64();
        void GetDouble();
        void GetGUID();
        void GetStringLength();
        void GetString();
        void GetAllocatedString();
        void GetBlobSize();
        void GetBlob();
        void GetAllocatedBlob();
        void GetUnknown();
        void SetItem();
        void DeleteItem();
        void DeleteAllItems();

        [PreserveSig]
        int SetUINT32([In] in Guid key, int value);

        [PreserveSig]
        int SetUINT64([In] in Guid key, long value);

        void SetDouble();

        [PreserveSig]
        int SetGUID([In] in Guid key, [In] in Guid value);
    }

    [ComImport]
    [Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSample
    {
        void GetItem();
        void GetItemType();
        void CompareItem();
        void Compare();
        void GetUINT32();
        void GetUINT64();
        void GetDouble();
        void GetGUID();
        void GetStringLength();
        void GetString();
        void GetAllocatedString();
        void GetBlobSize();
        void GetBlob();
        void GetAllocatedBlob();
        void GetUnknown();
        void SetItem();
        void DeleteItem();
        void DeleteAllItems();
        void SetUINT32();
        void SetUINT64();
        void SetDouble();
        void SetGUID();
        void SetString();
        void SetBlob();
        void SetUnknown();
        void LockStore();
        void UnlockStore();
        void GetCount();
        void GetItemByIndex();
        void CopyAllItems();
        void GetSampleFlags();
        void SetSampleFlags();
        void GetSampleTime();

        [PreserveSig]
        int SetSampleTime(long time);

        void GetSampleDuration();

        [PreserveSig]
        int SetSampleDuration(long duration);

        void GetBufferCount();
        void GetBufferByIndex();
        void ConvertToContiguousBuffer();

        [PreserveSig]
        int AddBuffer(IMFMediaBuffer buffer);
    }

    [ComImport]
    [Guid("045fa593-8799-42b8-bc8d-8968c6453507")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFMediaBuffer
    {
        [PreserveSig]
        int Lock(out IntPtr data, out int maxLength, out int currentLength);

        [PreserveSig]
        int Unlock();

        void GetCurrentLength();

        [PreserveSig]
        int SetCurrentLength(int length);
    }

    [ComImport]
    [Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSinkWriter
    {
        [PreserveSig]
        int AddStream(IMFMediaType type, out int stream);

        [PreserveSig]
        int SetInputMediaType(int stream, IMFMediaType type, IntPtr parameters);

        [PreserveSig]
        int BeginWriting();

        [PreserveSig]
        int WriteSample(int stream, IMFSample sample);

        void SendStreamTick();
        void PlaceMarker();
        void NotifyEndOfSegment();
        void Flush();

        [PreserveSig]
        int Finalize();
    }
}
