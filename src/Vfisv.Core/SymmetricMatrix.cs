namespace Vfisv;

internal static class SymmetricMatrix
{
    internal sealed record EigenSystem(double[] Values, double[,] Vectors);

    public static EigenSystem Jacobi(double[,] input, int maxSweeps = 80, double tolerance = 1e-14)
    {
        var n = input.GetLength(0);
        if (n != input.GetLength(1)) throw new ArgumentException("Matrix must be square.");
        var a = (double[,])input.Clone();
        var v = new double[n, n];
        for (var i = 0; i < n; i++) v[i, i] = 1.0;

        for (var sweep = 0; sweep < maxSweeps * n * n; sweep++)
        {
            var p = 0;
            var q = 1;
            var largest = 0.0;
            for (var i = 0; i < n; i++)
            for (var j = i + 1; j < n; j++)
            {
                var candidate = Math.Abs(a[i, j]);
                if (candidate > largest) { largest = candidate; p = i; q = j; }
            }

            var scale = 0.0;
            for (var i = 0; i < n; i++) scale = Math.Max(scale, Math.Abs(a[i, i]));
            if (largest <= tolerance * Math.Max(1.0, scale)) break;

            var angle = 0.5 * Math.Atan2(2.0 * a[p, q], a[q, q] - a[p, p]);
            var c = Math.Cos(angle);
            var s = Math.Sin(angle);
            var app = c * c * a[p, p] - 2.0 * s * c * a[p, q] + s * s * a[q, q];
            var aqq = s * s * a[p, p] + 2.0 * s * c * a[p, q] + c * c * a[q, q];

            for (var k = 0; k < n; k++)
            {
                if (k == p || k == q) continue;
                var akp = a[k, p];
                var akq = a[k, q];
                a[k, p] = a[p, k] = c * akp - s * akq;
                a[k, q] = a[q, k] = s * akp + c * akq;
            }
            a[p, p] = app;
            a[q, q] = aqq;
            a[p, q] = a[q, p] = 0.0;

            for (var k = 0; k < n; k++)
            {
                var vkp = v[k, p];
                var vkq = v[k, q];
                v[k, p] = c * vkp - s * vkq;
                v[k, q] = s * vkp + c * vkq;
            }
        }

        var values = new double[n];
        for (var i = 0; i < n; i++) values[i] = a[i, i];
        return new EigenSystem(values, v);
    }

    public static bool TryPseudoInverseSolve(double[,] matrix, double[] right,
        double relativeTolerance, out double[] solution)
    {
        var n = right.Length;
        solution = new double[n];
        if (matrix.GetLength(0) != n || matrix.GetLength(1) != n) return false;
        var eig = Jacobi(matrix);
        if (eig.Values.Any(x => !double.IsFinite(x))) return false;
        var maximum = eig.Values.Max(x => Math.Abs(x));
        if (!(maximum > 0.0)) return false;

        for (var k = 0; k < n; k++)
        {
            var eigenvalue = eig.Values[k];
            if (Math.Abs(eigenvalue) < relativeTolerance * maximum) continue;
            var projection = 0.0;
            for (var i = 0; i < n; i++) projection += eig.Vectors[i, k] * right[i];
            for (var i = 0; i < n; i++) solution[i] += eig.Vectors[i, k] * projection / eigenvalue;
        }
        return solution.All(double.IsFinite);
    }

    public static bool TryPseudoInverse(double[,] matrix, double relativeTolerance, out double[,] inverse)
    {
        var n = matrix.GetLength(0);
        inverse = new double[n, n];
        if (matrix.GetLength(1) != n) return false;
        var eig = Jacobi(matrix);
        if (eig.Values.Any(x => !double.IsFinite(x))) return false;
        var maximum = eig.Values.Max(x => Math.Abs(x));
        if (!(maximum > 0.0)) return false;

        for (var k = 0; k < n; k++)
        {
            var eigenvalue = eig.Values[k];
            if (Math.Abs(eigenvalue) < relativeTolerance * maximum) continue;
            for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
                inverse[i, j] += eig.Vectors[i, k] * eig.Vectors[j, k] / eigenvalue;
        }
        return true;
    }
}
