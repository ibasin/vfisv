using System.Globalization;

namespace FitsLib.Cli;

public class Program
{
    static async Task Main()
    {
        var time = DateTime.Parse("2/15/2012 3:24:00 PM");
        PixelRegion? region = new PixelRegion(1900, 1900, 64, 64);

        var inputImages = await FitsProvider.Load24InputImages(/*"input"*/"observation", time, region, false);

        var outputImages = new FloatImage[4];
        outputImages[0] = inputImages[0];
        outputImages[1] = inputImages[1];
        outputImages[2] = inputImages[2];
        outputImages[3] = inputImages[3];

        var prefix = time.ToString(CultureInfo.InvariantCulture);

        await FitsProvider.Save4OutputImages("output", prefix, outputImages);
    }
}