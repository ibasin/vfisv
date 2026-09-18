using FitsLib;

namespace GPULib;

public static class Vfisv
{
    public static FloatImage[] Process(FloatImage[,] inputImages)
    {
        if (inputImages.GetLength(0) < 4 || inputImages.GetLength(1) < 6)
        {
            throw new ArgumentException("Input images array must have at least 4 images in the first dimension and at least 6 images in the second dimension.");
        }

        //this is fake processing, we just copy 4 first inputs into outputs 
        var outputImages = new FloatImage[4];
        outputImages[0] = inputImages[0, 0];
        outputImages[1] = inputImages[1, 0];
        outputImages[2] = inputImages[2, 0];
        outputImages[3] = inputImages[3, 0];
        return outputImages;
    }
}
