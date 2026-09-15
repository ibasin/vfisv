using System.Numerics;

namespace Vfisv;

/// <summary>
/// Hui-Armstrong-Wray rational approximation used by the original voigt.f.
/// The dispersion result includes the VFISV factor-of-two convention.
/// </summary>
public static class VoigtProfile
{
    private static readonly double[] A =
    [
        122.607931777104326, 214.382388694706425, 181.928533092181549,
        93.155580458138441, 30.180142196210589, 5.912626209773153,
        0.564189583562615
    ];

    private static readonly double[] B =
    [
        122.60793177387535, 352.730625110963558, 457.334478783897737,
        348.703917719495792, 170.354001821091472, 53.992906912940207,
        10.479857114260399
    ];

    private static readonly double[] DawsonX =
    [0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0, 1.2, 1.4, 1.6, 1.8,
     2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0, 10.0, 12.0, 14.0, 16.0, 18.0, 20.0];

    private static readonly double[] DawsonY =
    [0.099335991, 0.19475104, 0.28263167, 0.35994348, 0.42443639, 0.47476321,
     0.51050407, 0.53210169, 0.54072434, 0.53807950, 0.50727350, 0.45650724,
     0.39993989, 0.34677279, 0.30134040, 0.17827103, 0.12934799, 0.10213407,
     0.084542692, 0.072180972, 0.063000202, 0.055905048, 0.050253846,
     0.041812878, 0.035806101, 0.031311397, 0.027820844, 0.025031367];

    public static (double Absorption, double Dispersion) Evaluate(double damping, double frequency)
    {
        var sign = frequency < 0.0 ? -1.0 : 1.0;
        var v = Math.Abs(frequency);

        if (damping == 0.0)
        {
            var v2 = v * v;
            var h = Math.Exp(-v2);
            double d;
            if (v <= DawsonX[0])
            {
                d = v * (1.0 - 0.66666667 * v2);
            }
            else if (v > DawsonX[^1])
            {
                var y = 0.5 / v;
                d = y * (1.0 + y / v);
            }
            else
            {
                var k = Array.FindIndex(DawsonX, x => v <= x);
                k = Math.Clamp(k, 1, DawsonX.Length - 2);
                var km = k - 1;
                var kp = k + 1;
                var d1 = v - DawsonX[km];
                var d2 = v - DawsonX[k];
                var d3 = v - DawsonX[kp];
                var d12 = DawsonX[km] - DawsonX[k];
                var d13 = DawsonX[km] - DawsonX[kp];
                var d23 = DawsonX[k] - DawsonX[kp];
                d = DawsonY[km] * d2 * d3 / (d12 * d13)
                    - DawsonY[k] * d1 * d3 / (d12 * d23)
                    + DawsonY[kp] * d1 * d2 / (d13 * d23);
            }

            return (h, 2.0 * sign * 0.5641895836 * d);
        }

        var z = new Complex(damping, -v);
        var numerator = new Complex(A[6], 0.0);
        for (var i = 5; i >= 0; i--) numerator = numerator * z + A[i];
        var denominator = z + B[6];
        for (var i = 5; i >= 0; i--) denominator = denominator * z + B[i];
        var ratio = numerator / denominator;
        return (ratio.Real, sign * ratio.Imaginary);
    }

    public static void Evaluate(double damping, ReadOnlySpan<double> frequencies,
        Span<double> absorption, Span<double> dispersion)
    {
        if (absorption.Length < frequencies.Length || dispersion.Length < frequencies.Length)
            throw new ArgumentException("Output spans must be at least as long as frequencies.");

        for (var i = 0; i < frequencies.Length; i++)
            (absorption[i], dispersion[i]) = Evaluate(damping, frequencies[i]);
    }
}
