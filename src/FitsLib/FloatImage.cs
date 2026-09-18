namespace FitsLib;

public sealed record FloatImage
{
    #region Constrcutors
    public FloatImage(int width, int height)
    {
        SetUp(width, height, new float[width * height]);
    }
    public FloatImage(int width, int height, float[] pixels)
    {
        SetUp(width, height, pixels);
    }
    private void SetUp(int width, int height, float[] pixels)
    {
        //validations
        //we are hardcoding these just in case since all images are this size, but we can change this later if needed

        if (width != 4096) throw new ArgumentOutOfRangeException(nameof(width));
        if (height != 4096) throw new ArgumentOutOfRangeException(nameof(height));

        //if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        //if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (pixels is null) throw new ArgumentNullException(nameof(pixels));
        if (pixels.Length != checked(width * height)) throw new ArgumentException("Pixel array length does not match width * height.", nameof(pixels));

        Width = width;
        Height = height;
        Pixels = pixels;
    }
    #endregion

    #region Custom Indexer
    public float this[int x, int y]
    {
        get
        {
            if (x < 0 || x >= Width) throw new ArgumentOutOfRangeException(nameof(x));
            if (y < 0 || y >= Height) throw new ArgumentOutOfRangeException(nameof(y));
            return Pixels[y * Width + x];
        }
        set
        {
            if (x < 0 || x >= Width) throw new ArgumentOutOfRangeException(nameof(x));
            if (y < 0 || y >= Height) throw new ArgumentOutOfRangeException(nameof(y));
            Pixels[y * Width + x] = value;
        }
    }
    #endregion

    #region Properties
    public int Width { get; private set; }
    public int Height { get; private set; }
    public float[] Pixels { get; private set; } = null!;
    #endregion
}