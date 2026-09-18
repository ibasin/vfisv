using GPULib;
using System.Globalization;

namespace FitsLib.Cli;

public class Program
{
    static async Task Main()
    {
        var time = DateTime.Parse("2/15/2012 3:24:00 PM");
        var inputImages = await FitsProvider.Load24InputImages("input", time, false);
        var outputImages = Vfisv.ProcessOnGpu(inputImages);
        var prefix = time.ToString(CultureInfo.InvariantCulture).Replace("/", "-").Replace("\\", "-").Replace(" ", "-").Replace(":", "-");
        await FitsProvider.Save4OutputImages("output", prefix, outputImages);
    }
}