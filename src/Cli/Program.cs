using FitsLib;
using GPULib;
using System.Diagnostics;
using System.Globalization;

namespace Cli;

public class Program
{
    static async Task Main()
    {
        var time = DateTime.Parse("2/15/2012 3:24:00 PM");
        var inputImages = await FitsProvider.Load24InputImages("input", time, false);
        var outputImages = Vfisv.ProcessOnGpu(inputImages);

        #if DEBUG
        var x = 2000;
        var y = 2000;
        var sum = 0f;
        for (var i = 0; i < 6; i++)
        {
            sum = inputImages[i][x, y];
        }
        const float tolerance = 0.0001f;
        Debug.Assert(Math.Abs(outputImages[0][x,y] - sum) < tolerance);
        Debug.Assert(Math.Abs(outputImages[1][x, y] - sum - 1) < tolerance);
        Debug.Assert(Math.Abs(outputImages[2][x, y] - sum - 2) < tolerance);
        Debug.Assert(Math.Abs(outputImages[3][x, y] - sum - 3) < tolerance);
        #endif

        var prefix = time.ToString(CultureInfo.InvariantCulture).Replace("/", "-").Replace("\\", "-").Replace(" ", "-").Replace(":", "-");
        await FitsProvider.Save4OutputImages("output", prefix, outputImages);
    }
}