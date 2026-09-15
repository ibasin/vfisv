using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Vfisv;

/// <summary>Small dependency-free reader/writer for uncompressed primary FITS images.</summary>
public sealed class FitsPrimaryImage
{
    public required IReadOnlyDictionary<string, string> Header { get; init; }
    public required int[] Axes { get; init; }
    public required float[] Data { get; init; }

    public int GetInt32(string key) => int.Parse(Header[key], CultureInfo.InvariantCulture);

    public static FitsPrimaryImage Read(string path)
    {
        using var stream = File.OpenRead(path);
        var header = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var card = new byte[80];
        long headerBytes = 0;
        while (true)
        {
            stream.ReadExactly(card); headerBytes += 80;
            var text = Encoding.ASCII.GetString(card);
            var keyword = text[..8].Trim();
            if (keyword == "END") break;
            if (text.Length > 10 && text[8] == '=')
            {
                var raw = text[10..];
                var slash = raw.IndexOf('/');
                var value = (slash >= 0 ? raw[..slash] : raw).Trim().Trim('\'');
                header[keyword] = value;
            }
        }
        var remainder = headerBytes % 2880;
        if (remainder != 0) stream.Position += 2880 - remainder;
        var bitpix = int.Parse(header["BITPIX"], CultureInfo.InvariantCulture);
        if (bitpix != -32) throw new NotSupportedException($"Only BITPIX=-32 primary images are supported; got {bitpix}.");
        var axisCount = int.Parse(header["NAXIS"], CultureInfo.InvariantCulture);
        var axes = Enumerable.Range(1, axisCount).Select(i => int.Parse(header[$"NAXIS{i}"], CultureInfo.InvariantCulture)).ToArray();
        var count = axes.Aggregate(1, checked((a, b) => a * b));
        var bytes = new byte[count * 4]; stream.ReadExactly(bytes);
        var data = new float[count];
        for (var i = 0; i < count; i++)
            data[i] = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(i * 4, 4)));
        return new FitsPrimaryImage { Header = header, Axes = axes, Data = data };
    }

    public static void WriteFloat32(string path, int width, int height, ReadOnlySpan<float> data)
    {
        if (data.Length != width * height) throw new ArgumentException("Data size does not match image dimensions.");
        using var stream = File.Create(path);
        var cards = new List<string>
        {
            Card("SIMPLE", "T", "file conforms to FITS standard"), Card("BITPIX", "-32", "32-bit IEEE float"),
            Card("NAXIS", "2", null), Card("NAXIS1", width.ToString(CultureInfo.InvariantCulture), null),
            Card("NAXIS2", height.ToString(CultureInfo.InvariantCulture), null), "END".PadRight(80)
        };
        var header = Encoding.ASCII.GetBytes(string.Concat(cards));
        stream.Write(header); WritePadding(stream, header.Length, 0x20);
        Span<byte> bytes = stackalloc byte[4];
        foreach (var value in data)
        {
            BinaryPrimitives.WriteInt32BigEndian(bytes, BitConverter.SingleToInt32Bits(value)); stream.Write(bytes);
        }
        WritePadding(stream, data.Length * 4, 0x00);
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
}
