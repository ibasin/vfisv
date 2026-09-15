using nom.tam.fits;

namespace Vfisv;

public sealed record FitsFloatImage(int Width, int Height, float[] Pixels);

/// <summary>
/// Reads ordinary FITS images and the RICE_1 tile-compressed binary-table images
/// produced by JSOC. CSharpFITS supplies FITS/HDU/table parsing; its 2008-era
/// implementation does not include the tile-compression codecs, so RICE_1 tiles
/// are decoded here.
/// </summary>
public static class CSharpFitsImageReader
{
    public static FitsFloatImage Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fits = new Fits(path, FileAccess.Read);
        try
        {
            BasicHDU? hdu;
            while ((hdu = fits.ReadHDU()) is not null)
            {
                if (hdu.Header.GetBooleanValue("ZIMAGE", false))
                {
                    if (hdu is not BinaryTableHDU table)
                        throw new InvalidDataException("A compressed FITS image must be stored in a binary-table HDU.");
                    return ReadRiceImage(table);
                }

                if (hdu is ImageHDU image && image.Header.GetIntValue("NAXIS", 0) == 2)
                    return ReadOrdinaryImage(image);
            }

            throw new InvalidDataException("The FITS file contains no two-dimensional image.");
        }
        finally
        {
            // CSharpFITS 1.1.0 can throw from Close() after a failed legacy stream
            // initialization; do not let that hide the useful parsing exception.
            try { fits.Close(); }
            catch (NullReferenceException) { }
        }
    }

    private static FitsFloatImage ReadRiceImage(BinaryTableHDU table)
    {
        var header = table.Header;
        var compression = header.GetStringValue("ZCMPTYPE")?.Trim();
        if (!string.Equals(compression, "RICE_1", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(compression, "RICE_ONE", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"Only RICE_1 compressed FITS images are supported; got {compression ?? "<missing>"}.");

        var bitpix = header.GetIntValue("ZBITPIX", 0);
        var dimensions = header.GetIntValue("ZNAXIS", 0);
        var width = header.GetIntValue("ZNAXIS1", 0);
        var height = header.GetIntValue("ZNAXIS2", 0);
        var tileWidth = header.GetIntValue("ZTILE1", width);
        var tileHeight = header.GetIntValue("ZTILE2", 1);
        var blockSize = ReadCompressionParameter(header, "BLOCKSIZE", 32);
        var bytesPerPixel = ReadCompressionParameter(header, "BYTEPIX", Math.Abs(bitpix) / 8);

        if (dimensions != 2 || width <= 0 || height <= 0)
            throw new NotSupportedException("Only non-empty two-dimensional compressed FITS images are supported.");
        if (bitpix != 16 || bytesPerPixel != 2)
            throw new NotSupportedException($"Only 16-bit RICE_1 pixels are supported; got ZBITPIX={bitpix}, BYTEPIX={bytesPerPixel}.");
        if (tileWidth != width || tileHeight != 1 || table.NRows != height)
            throw new NotSupportedException("This reader currently requires one complete image row per compressed tile.");

        var column = table.FindColumn("COMPRESSED_DATA");
        if (column < 0) throw new InvalidDataException("The compressed image has no COMPRESSED_DATA column.");

        var blank = header.ContainsKey("BLANK") ? header.GetIntValue("BLANK") : int.MinValue;
        var scale = header.GetDoubleValue("BSCALE", 1.0);
        var zero = header.GetDoubleValue("BZERO", 0.0);
        var pixels = new float[checked(width * height)];

        for (var row = 0; row < height; row++)
        {
            if (table.GetElement(row, column) is not byte[] compressed)
                throw new InvalidDataException($"Compressed tile {row} is not a byte array.");
            var raw = RiceCodec.DecodeInt16(compressed, width, blockSize);
            var offset = row * width;
            for (var x = 0; x < width; x++)
            {
                var stored = unchecked((short)raw[x]);
                pixels[offset + x] = stored == blank ? float.NaN : (float)(stored * scale + zero);
            }
        }

        return new FitsFloatImage(width, height, pixels);
    }

    private static FitsFloatImage ReadOrdinaryImage(ImageHDU image)
    {
        var header = image.Header;
        var width = header.GetIntValue("NAXIS1", 0);
        var height = header.GetIntValue("NAXIS2", 0);
        if (width <= 0 || height <= 0) throw new InvalidDataException("Invalid FITS image dimensions.");

        var scale = header.GetDoubleValue("BSCALE", 1.0);
        var zero = header.GetDoubleValue("BZERO", 0.0);
        var blank = header.ContainsKey("BLANK") ? header.GetIntValue("BLANK") : int.MinValue;
        var pixels = new float[checked(width * height)];
        FlattenAndScale(image.Kernel, pixels, scale, zero, blank);
        return new FitsFloatImage(width, height, pixels);
    }

    private static int ReadCompressionParameter(Header header, string name, int defaultValue)
    {
        for (var index = 1; index <= 99; index++)
        {
            var parameter = header.GetStringValue($"ZNAME{index}")?.Trim();
            if (parameter is null) break;
            if (string.Equals(parameter, name, StringComparison.OrdinalIgnoreCase))
                return header.GetIntValue($"ZVAL{index}", defaultValue);
        }
        return defaultValue;
    }

    private static void FlattenAndScale(object kernel, Span<float> destination, double scale, double zero, int blank)
    {
        if (kernel is not Array array) throw new NotSupportedException("The FITS image kernel is not an array.");
        var index = 0;
        FlattenAndScaleCore(array, destination, scale, zero, blank, ref index);
        if (index != destination.Length) throw new InvalidDataException("FITS image pixel count does not match its dimensions.");
    }

    private static void FlattenAndScaleCore(Array array, Span<float> destination, double scale, double zero, int blank, ref int index)
    {
        foreach (var value in array)
        {
            if (value is Array nested)
            {
                FlattenAndScaleCore(nested, destination, scale, zero, blank, ref index);
                continue;
            }

            if (index >= destination.Length) throw new InvalidDataException("FITS image contains too many pixels.");
            var raw = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
            destination[index++] = raw == blank ? float.NaN : (float)(raw * scale + zero);
        }
    }
}

/// <summary>Decoder for the FITS RICE_1 integer codec, using MSB-first packed bits.</summary>
internal static class RiceCodec
{
    public static ushort[] DecodeInt16(ReadOnlySpan<byte> compressed, int pixelCount, int blockSize)
    {
        if (pixelCount < 0) throw new ArgumentOutOfRangeException(nameof(pixelCount));
        if (blockSize <= 0) throw new ArgumentOutOfRangeException(nameof(blockSize));
        if (pixelCount == 0) return [];
        if (compressed.Length < 2) throw new InvalidDataException("RICE_1 tile is missing its initial pixel.");

        const int fsBits = 4;
        const int directCode = 14;
        var lastPixel = (uint)((compressed[0] << 8) | compressed[1]);
        var bits = new RiceBitReader(compressed[2..]);
        var result = new ushort[pixelCount];

        for (var index = 0; index < pixelCount;)
        {
            var fs = (int)bits.ReadBits(fsBits) - 1;
            var end = Math.Min(index + blockSize, pixelCount);

            if (fs < 0)
            {
                while (index < end) result[index++] = (ushort)lastPixel;
                continue;
            }

            while (index < end)
            {
                uint mapped;
                if (fs == directCode)
                {
                    mapped = bits.ReadBits(16);
                }
                else
                {
                    var quotient = bits.ReadUnaryZeros();
                    if (quotient > (uint.MaxValue >> fs))
                        throw new InvalidDataException("RICE_1 difference exceeds the supported range.");
                    mapped = (quotient << fs) | bits.ReadBits(fs);
                }

                var difference = (mapped & 1) == 0
                    ? unchecked((int)(mapped >> 1))
                    : unchecked(~(int)(mapped >> 1));
                lastPixel = unchecked((ushort)(lastPixel + difference));
                result[index++] = (ushort)lastPixel;
            }
        }

        return result;
    }

    private ref struct RiceBitReader(ReadOnlySpan<byte> source)
    {
        private readonly ReadOnlySpan<byte> _source = source;
        private int _bitOffset;

        public uint ReadBits(int count)
        {
            if (count is < 0 or > 32) throw new ArgumentOutOfRangeException(nameof(count));
            uint value = 0;
            for (var i = 0; i < count; i++) value = (value << 1) | ReadBit();
            return value;
        }

        public uint ReadUnaryZeros()
        {
            uint zeros = 0;
            while (ReadBit() == 0)
            {
                if (zeros == uint.MaxValue) throw new InvalidDataException("Invalid RICE_1 unary value.");
                zeros++;
            }
            return zeros;
        }

        private uint ReadBit()
        {
            if (_bitOffset >= _source.Length * 8) throw new InvalidDataException("Unexpected end of RICE_1 tile.");
            var value = (uint)((_source[_bitOffset >> 3] >> (7 - (_bitOffset & 7))) & 1);
            _bitOffset++;
            return value;
        }
    }
}
