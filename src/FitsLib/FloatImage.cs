namespace FitsLib;

public sealed record FloatImage
{
    #region Constrcutors
    public FloatImage(int width, int height, float[] pixels)
    {
        //validations
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
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
    }
    #endregion

    #region Properties
    public int Width { get; }
    public int Height { get; }
    public float[] Pixels { get; }
    #endregion
}