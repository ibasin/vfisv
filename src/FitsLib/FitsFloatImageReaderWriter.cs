using nom.tam.fits;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace FitsLib;

// Reads ordinary FITS images and the RICE_1 tile-compressed binary-table images produced by JSOC.CSharpFITS supplies FITS/HDU/table parsing; its 2008-era
// implementation does not include the tile-compression codecs, so RICE_1 tiles are decoded here.
public static class FitsFloatImageReaderWriter
{
    #region Read All Images in input Dir
    public static FloatImage[] Read24InputFitsImagesInDirectory(string inputDir, HmiObservationMetadata metadata)
    {
        Console.WriteLine("\n***** Starting to read all *.fits images into memory *****");
        ArgumentException.ThrowIfNullOrWhiteSpace(inputDir);
        var images = new FloatImage[ExpectedSegmentNames.Length];

        for (var segmentIdx = 0; segmentIdx < ExpectedSegmentNames.Length; segmentIdx++)
        {
            var path = Path.Combine(inputDir, ExpectedSegmentNames[segmentIdx] + ".fits");
            if (!File.Exists(path)) throw new FileNotFoundException($"Missing HMI segment {ExpectedSegmentNames[segmentIdx]}.", path);
            var image = Read(path);
            if (image.Width != metadata.Width || image.Height != metadata.Height) throw new InvalidDataException($"{Path.GetFileName(path)} is {image.Width}x{image.Height}; expected {metadata.Width}x{metadata.Height}.");
            images[segmentIdx] = image;
        }
        Console.WriteLine("***** Done reading all *.fits images into memory *****");

        return images;
    }
    private static readonly string[] ExpectedSegmentNames =
    [
        "I0", "I1", "I2", "I3", "I4", "I5",
        "Q0", "Q1", "Q2", "Q3", "Q4", "Q5",
        "U0", "U1", "U2", "U3", "U4", "U5",
        "V0", "V1", "V2", "V3", "V4", "V5"
    ];
    #endregion

    #region Read Single Image
    public static FloatImage Read(string fullFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullFileName);
        var fits = new Fits(fullFileName, FileAccess.Read);
        try
        {
            while (fits.ReadHDU() is { } hdu)
            {
                if (hdu.Header.GetBooleanValue("ZIMAGE", false))
                {
                    if (hdu is not BinaryTableHDU table) throw new InvalidDataException("A compressed FITS image must be stored in a binary-table HDU.");
                    return ReadRiceImage(table);
                }

                if (hdu is ImageHDU image && image.Header.GetIntValue("NAXIS", 0) == 2) return ReadOrdinaryImage(image);
            }

            throw new InvalidDataException("The FITS file contains no two-dimensional image.");
        }
        finally
        {
            // CSharpFITS 1.1.0 can throw from Close() after a failed legacy stream initialization; do not let that hide the useful parsing exception.
            try { fits.Close(); }
            catch (NullReferenceException) { }
        }
    }

    private static FloatImage ReadRiceImage(BinaryTableHDU table)
    {
        var header = table.Header;
        var compression = header.GetStringValue("ZCMPTYPE")?.Trim();
        if (!string.Equals(compression, "RICE_1", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(compression, "RICE_ONE", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"Only RICE_1 compressed FITS images are supported; got {compression ?? "<missing>"}.");
        }

        var bitpix = header.GetIntValue("ZBITPIX", 0);
        var dimensions = header.GetIntValue("ZNAXIS", 0);
        var width = header.GetIntValue("ZNAXIS1", 0);
        var height = header.GetIntValue("ZNAXIS2", 0);
        var tileWidth = header.GetIntValue("ZTILE1", width);
        var tileHeight = header.GetIntValue("ZTILE2", 1);
        var blockSize = ReadCompressionParameter(header, "BLOCKSIZE", 32);
        var bytesPerPixel = ReadCompressionParameter(header, "BYTEPIX", Math.Abs(bitpix) / 8);

        if (dimensions != 2 || width <= 0 || height <= 0) throw new NotSupportedException("Only non-empty two-dimensional compressed FITS images are supported.");
        if (bitpix != 16 || bytesPerPixel != 2) throw new NotSupportedException($"Only 16-bit RICE_1 pixels are supported; got ZBITPIX={bitpix}, BYTEPIX={bytesPerPixel}.");
        if (tileWidth != width || tileHeight != 1 || table.NRows != height) throw new NotSupportedException("This reader currently requires one complete image row per compressed tile.");

        var column = table.FindColumn("COMPRESSED_DATA");
        if (column < 0) throw new InvalidDataException("The compressed image has no COMPRESSED_DATA column.");

        var blank = header.ContainsKey("BLANK") ? header.GetIntValue("BLANK") : int.MinValue;
        var scale = header.GetDoubleValue("BSCALE", 1.0);
        var zero = header.GetDoubleValue("BZERO", 0.0);
        var pixels = new float[checked(width * height)];

        for (var row = 0; row < height; row++)
        {
            if (table.GetElement(row, column) is not byte[] compressed) throw new InvalidDataException($"Compressed tile {row} is not a byte array.");
            var raw = RiceCodec.DecodeInt16(compressed, width, blockSize);
            var offset = row * width;
            for (var x = 0; x < width; x++)
            {
                var stored = unchecked((short)raw[x]);
                pixels[offset + x] = stored == blank ? float.NaN : (float)(stored * scale + zero);
            }
        }

        return new FloatImage(width, height, pixels);
    }
    private static FloatImage ReadOrdinaryImage(ImageHDU image)
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
        return new FloatImage(width, height, pixels);
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
    #endregion

    #region Write Single Image
    public static void Write(string fullFileName, FloatImage image)
    {
        using var stream = File.Create(fullFileName);
        var cards = new List<string>
        {
            Card("SIMPLE", "T", "file conforms to FITS standard"), Card("BITPIX", "-32", "32-bit IEEE float"),
            Card("NAXIS", "2", null), Card("NAXIS1", image.Width.ToString(CultureInfo.InvariantCulture), null),
            Card("NAXIS2", image.Height.ToString(CultureInfo.InvariantCulture), null), "END".PadRight(80)
        };
        var header = Encoding.ASCII.GetBytes(string.Concat(cards));
        stream.Write(header); WritePadding(stream, header.Length, 0x20);
        Span<byte> bytes = stackalloc byte[4];
        foreach (var value in image.Pixels)
        {
            BinaryPrimitives.WriteInt32BigEndian(bytes, BitConverter.SingleToInt32Bits(value)); stream.Write(bytes);
        }
        WritePadding(stream, image.Pixels.Length * 4, 0x00);
    }
    private static string Card(string key, string value, string? comment)
    {
        var text = $"{key,-8}= {value,20}";
        if (comment is not null) text += $" / {comment}";
        return text.PadRight(80)[..80];
    }
    private static void WritePadding(Stream stream, int length, byte value)
    {
        var padding = (2880 - length % 2880) % 2880;
        if (padding > 0)
        {
            var bytes = new byte[padding];
            if (value != 0) Array.Fill(bytes, value);
            stream.Write(bytes);
        }
    }
    #endregion
}