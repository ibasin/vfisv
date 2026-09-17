namespace FitsLib;

public sealed record FloatImage(int Width, int Height, float[] Pixels)
{
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
}