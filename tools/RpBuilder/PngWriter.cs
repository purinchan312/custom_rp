using System.Buffers.Binary;
using System.IO.Compression;

namespace RpBuilder;

/// <summary>
/// .NET 10 標準ライブラリ (System.IO.Compression.ZLibStream) のみを用いた
/// 外部依存ゼロ・完全確定出力の 32-bit RGBA PNG エンコーダー
/// </summary>
public static class PngWriter
{
    private static readonly uint[] CrcTable = CreateCrc32Table();

    public static void WriteRgba(string filePath, int width, int height, ReadOnlySpan<byte> rgbaBytes)
    {
        if (rgbaBytes.Length != width * height * 4)
        {
            throw new ArgumentException($"Invalid RGBA buffer length: {rgbaBytes.Length}, expected {width * height * 4}.");
        }

        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        int stride = width * 4;
        byte[] rawScanlines = new byte[height * (stride + 1)];
        for (int y = 0; y < height; y++)
        {
            int dstRow = y * (stride + 1);
            rawScanlines[dstRow] = 0; // Filter type 0 (None)
            rgbaBytes.Slice(y * stride, stride).CopyTo(rawScanlines.AsSpan(dstRow + 1, stride));
        }

        byte[] compressedIdat;
        using (var idatMs = new MemoryStream())
        {
            using (var zlib = new ZLibStream(idatMs, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                zlib.Write(rawScanlines, 0, rawScanlines.Length);
            }
            compressedIdat = idatMs.ToArray();
        }

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[0..4], (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[4..8], (uint)height);
        ihdr[8] = 8;  // Bit depth: 8
        ihdr[9] = 6;  // Color type: 6 (Truecolor with alpha RGBA)
        ihdr[10] = 0; // Compression: Deflate
        ihdr[11] = 0; // Filter: Adaptive
        ihdr[12] = 0; // Interlace: None

        using var outFs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        ReadOnlySpan<byte> pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        outFs.Write(pngSignature);

        WriteChunk(outFs, "IHDR"u8, ihdr);
        WriteChunk(outFs, "IDAT"u8, compressedIdat);
        WriteChunk(outFs, "IEND"u8, ReadOnlySpan<byte>.Empty);
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> chunkType, ReadOnlySpan<byte> data)
    {
        Span<byte> lenBuf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(lenBuf, (uint)data.Length);
        stream.Write(lenBuf);
        stream.Write(chunkType);
        if (!data.IsEmpty)
        {
            stream.Write(data);
        }

        uint crc = 0xFFFFFFFFu;
        crc = UpdateCrc32(crc, chunkType);
        crc = UpdateCrc32(crc, data);
        crc ^= 0xFFFFFFFFu;

        Span<byte> crcBuf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBuf, crc);
        stream.Write(crcBuf);
    }

    private static uint[] CreateCrc32Table()
    {
        uint[] table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : (c >> 1);
            }
            table[n] = c;
        }
        return table;
    }

    private static uint UpdateCrc32(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }
        return crc;
    }
}
