using GPULib;
using System.Globalization;

namespace FitsLib.Cli;

public class Program
{
    static async Task Main()
    {
        var time = DateTime.Parse("2/15/2012 3:24:00 PM");
        PixelRegion? region = new PixelRegion(1900, 1900, 64, 64);

        var inputImages = await FitsProvider.Load24InputImages("input", time, region, false);
        Console.WriteLine(inputImages[0][2000, 2000]);

        var outputImages = Vfisv.ProcessOnGpu(inputImages);

        Console.WriteLine(outputImages[0][2000, 2000]);

        var prefix = time.ToString(CultureInfo.InvariantCulture).Replace("/", "-").Replace("\\", "-").Replace(" ", "-").Replace(":", "-");
        await FitsProvider.Save4OutputImages("output", prefix, outputImages);
    }
}