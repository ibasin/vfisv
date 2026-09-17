namespace FitsLib;

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
                    if (quotient > uint.MaxValue >> fs) throw new InvalidDataException("RICE_1 difference exceeds the supported range.");
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