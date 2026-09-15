namespace Vfisv;

/// <summary>Thread-safe entry point for single-pixel VFISV inversion.</summary>
public sealed class VfisvEngine
{
    private readonly VfisvOptions _options;

    public VfisvEngine(VfisvOptions? options = null) => _options = options ?? new VfisvOptions();

    /// <summary>Synthesizes filtered Stokes profiles for a model returned by <see cref="Invert"/>.</summary>
    public double[] Synthesize(double[,] filters, double[] scatteredLight, double[] model,
        bool modelUsesResultAzimuthConvention = true)
    {
        if (model.Length != 10) throw new ArgumentException("VFISV uses ten model parameters.", nameof(model));
        var bins = filters.GetLength(1); var fullWavelengths = filters.GetLength(0);
        if (bins is not (5 or 6 or 8 or 10)) throw new ArgumentException("Filter count must be 5, 6, 8, or 10.", nameof(filters));
        if (scatteredLight.Length is not 0 && scatteredLight.Length != 4 * bins) throw new ArgumentException("Scattered-light size is inconsistent.", nameof(scatteredLight));
        var free = Enumerable.Repeat(true, 10).ToArray();
        var context = CreateContext(bins, fullWavelengths, free, Enumerable.Range(0, 10).ToArray(), model[7] + model[8], 1.0);
        var (inner, outer) = SplitFilters(filters, context.WavelengthCount);
        var scattered = scatteredLight.Length == 0 ? new double[bins, 4] : Unpack(scatteredLight, bins);
        var internalModel = (double[])model.Clone();
        if (modelUsesResultAzimuthConvention)
        {
            internalModel[2] -= 90.0;
            if (internalModel[2] < 0.0) internalModel[2] += 180.0;
        }
        return Pack(ForwardModel.Synthesize(internalModel, scattered, false, inner, outer, context, _options).Stokes);
    }

    public InversionResult Invert(InversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);
        var bins = request.Filters.GetLength(1);
        var fullWavelengths = request.Filters.GetLength(0);
        var guess = (double[])request.InitialGuess.Clone();
        var scatteredLong = request.ScatteredLight.Length == 0 ? new double[4 * bins] : request.ScatteredLight;
        var observed = UnpackAndReverse(request.Observed, bins);
        var scattered = UnpackAndReverse(scatteredLong, bins);
        var continuum = Enumerable.Range(0, bins).Max(i => request.Observed[i]);
        var averageIntensity = Enumerable.Range(0, bins).Average(i => request.Observed[i]);
        var noiseScale = Math.Sqrt(averageIntensity);
        var free = request.FreeParameters.Select(x => x == 1).ToArray();
        var freeLocations = Enumerable.Range(0, 10).Where(i => free[i]).ToArray();
        var context = CreateContext(bins, fullWavelengths, free, freeLocations, continuum, noiseScale);
        var (filters, integratedFilters) = SplitFilters(request.Filters, context.WavelengthCount);

        if (!(continuum > _options.IntensityThreshold))
            return Failed(ConvergenceStatus.IntensityTooLow, 0, bins);

        var weights = (double[])_options.Weights.Clone();
        var maxWeight = weights.Max();
        for (var i = 0; i < 4; i++) weights[i] = weights[i] / maxWeight / context.Noise[i];
        WeakFieldGuess(observed, guess, context, _options);
        if (!free[3]) guess[3] = 0.5;
        if (!free[9]) guess[9] = 1.0;
        FineTune(guess, context);

        var model = (double[])guess.Clone();
        var bestModel = (double[])guess.Clone();
        var lastGoodModel = (double[])guess.Clone();
        var bestMinimumModel = new double[10];
        var lastGoodSynthesis = new double[bins, 4];
        var bestSynthesis = new double[bins, 4];
        var lastGoodDerivatives = new double[10, bins, 4];
        var bestChi2 = 1e24;
        var bestMinimumChi2 = 1e24;
        var lastGoodChi2 = 1e24;
        var lambda = 0.1;
        var iteration = 1;
        var iterationStart = 1;
        var resetCount = 0;
        var resetFlag = 0;
        var done = false;
        var random = new Random(_options.RandomSeed);

        while (iteration < _options.Iterations && !done)
        {
            if (resetFlag != 0)
            {
                bestMinimumChi2 = bestChi2;
                Array.Copy(bestModel, bestMinimumModel, 10);
                iterationStart = iteration;
                model = (double[])bestModel.Clone();
                RandomModelJump(model, context, random);
                if (resetCount % 2 == 0)
                {
                    if (bestModel[5] >= 300.0)
                    {
                        model = (double[])bestModel.Clone();
                        if (bestModel[5] > 1000.0) { model[0] = 50.0; model[4] = 12.0; }
                        else
                        {
                            var sourceRatio = resetCount % 4 == 2 ? 0.2 : 1.0;
                            model[0] = resetCount % 4 == 2 ? 10.0 : 9.0;
                            model[4] = resetCount % 4 == 2 ? 7.0 : 30.0;
                            SetSourceRatio(model, bestModel[7] + bestModel[8], sourceRatio);
                        }
                    }
                    if (bestModel[5] < 300.0)
                    {
                        model = (double[])bestModel.Clone();
                        model[0] = 8.0;
                        model[4] = 0.75 * bestModel[4];
                        model[5] = 85.0;
                        SetSourceRatio(model, bestModel[7] + bestModel[8], 0.017 * bestModel[4]);
                    }
                }
                FineTune(model, context);
                lastGoodModel = (double[])model.Clone();
                lastGoodChi2 = 1e24;
                lambda = 0.1;
                resetCount++;
            }

            var synthesis = ForwardModel.Synthesize(model, scattered, false, filters, integratedFilters, context, _options);
            var newChi2 = ChiSquared(model, synthesis.Stokes, observed, weights, context, _options.UseRegularization);
            if (!double.IsFinite(newChi2)) newChi2 = 2.0 * lastGoodChi2;
            var deltaChi2 = lastGoodChi2 - newChi2;

            if (deltaChi2 >= 0.0)
            {
                synthesis = ForwardModel.Synthesize(model, scattered, true, filters, integratedFilters, context, _options);
                var derivatives = synthesis.Derivatives;
                if (_options.UseVariableChange) ChangeDerivatives(model, derivatives, bins);
                NormalizeAndMaskDerivatives(derivatives, context);
                lastGoodModel = (double[])model.Clone();
                lastGoodChi2 = newChi2;
                lastGoodSynthesis = (double[,])synthesis.Stokes.Clone();
                lastGoodDerivatives = (double[,,])derivatives.Clone();
                if (newChi2 < bestChi2)
                {
                    bestModel = (double[])model.Clone();
                    bestChi2 = newChi2;
                    bestSynthesis = (double[,])synthesis.Stokes.Clone();
                }
            }
            else
            {
                model = (double[])lastGoodModel.Clone();
            }

            lambda = NextLambda(deltaChi2, lambda);
            var divergence = Divergence(lastGoodSynthesis, observed, lastGoodDerivatives, weights, context);
            var hessian = Hessian(lastGoodDerivatives, weights, context);
            var deltaModel = ModelDelta(lastGoodModel, divergence, hessian, lambda, context);
            NormalizeDelta(deltaModel, context);
            if (_options.UseVariableChange) UndoFiniteVariableChange(model, deltaModel);
            CutDelta(deltaModel, model, context);
            for (var p = 0; p < 10; p++) model[p] = lastGoodModel[p] + deltaModel[p];
            FineTune(model, context);

            var abandon = resetCount == 1 && iteration - iterationStart >= 5
                          && lastGoodChi2 - bestMinimumChi2 > 0.0 && bestModel[5] < 300.0;
            resetFlag = 0;
            if (lambda >= 100.0 || abandon)
            {
                if (resetCount == 0)
                {
                    if (bestModel[5] < 30.0 || bestModel[5] > 300.0 || bestChi2 > 20.0) resetFlag = 1;
                    if (bestChi2 > 2.75 && bestModel[5] < 70.0 && bestModel[4] < 15.0) resetFlag = 1;
                    if (Math.Abs(bestModel[1] - 90.0) > 10.0 * Math.Log(bestModel[5] / 7.0)) resetFlag = 1;
                    if (bestModel[0] < 5.0 && bestModel[4] > 20.0 && bestModel[5] < 300.0) resetFlag = 2;
                }
                var lowField = 35.0 - resetCount;
                if (Math.Abs(bestModel[1] - 90.0) > 60.0 || bestModel[5] < lowField || bestModel[5] > 500.0) resetFlag = 1;
                if (bestModel[0] < 3.5 && bestModel[4] > 30.0 && bestModel[5] < 45.0) resetFlag = 2;
                if (resetFlag == 0) done = true;
            }
            iteration++;
        }

        if (bestChi2 > 1e20) return Failed(ConvergenceStatus.NoFiniteFit, iteration, bins);

        var finalDerivatives = ForwardModel.Synthesize(bestModel, scattered, true, filters, integratedFilters, context, _options).Derivatives;
        NormalizeAndMaskDerivatives(finalDerivatives, context);
        var finalHessian = Hessian(finalDerivatives, weights, context);
        var (errors, errorOk) = CalculateErrors(finalHessian, bestChi2, context);
        errors[11] = bestChi2;
        var status = errorOk ? ConvergenceStatus.Converged : ConvergenceStatus.ErrorEstimationFailed;
        if (iteration == _options.Iterations && resetCount == 0) status = ConvergenceStatus.MaximumIterationsReached;

        var resultModel = (double[])bestModel.Clone();
        resultModel[2] += 90.0;
        if (resultModel[2] > 180.0) resultModel[2] -= 180.0;
        return new InversionResult
        {
            Model = resultModel,
            Errors = errors,
            SyntheticStokes = Pack(bestSynthesis),
            Status = status,
            Iterations = iteration,
            ChiSquared = bestChi2
        };
    }

    private InversionContext CreateContext(int bins, int fullWavelengths, bool[] free, int[] freeLocations,
        double continuum, double noiseScale)
    {
        var nw = _options.SyntheticWavelengthCount;
        var wave = Enumerable.Range(0, nw)
            .Select(i => _options.SyntheticMinimumMilliAngstrom + i * _options.WavelengthStepMilliAngstrom).ToArray();
        var tune = Enumerable.Range(0, bins)
            .Select(i => (-(bins - 1.0) / 2.0 + i) * 69.0).ToArray();
        return new InversionContext
        {
            BinCount = bins, WavelengthCount = nw, FullWavelengthCount = fullWavelengths,
            Free = free, FreeLocations = freeLocations, FreeDegrees = 4 * bins - freeLocations.Length,
            Wave = wave, TunePositions = tune,
            Noise = _options.NoiseFactors.Select(x => x * noiseScale).ToArray(), Continuum = continuum,
            LowerLimit = [1.0, 0.0, 0.0, 1e-4, 1.0, 5.0, -7e5, 0.15 * continuum, 0.15 * continuum, 0.0],
            UpperLimit = [1e3, 180.0, 180.0, 5.0, 5e2, 5e3, 7e5, 1.2 * continuum, 1.2 * continuum, 1.0],
            Norm = [0.2, 7.0, 50.0, 1.0, 0.4, 50.0, 5000.0, 0.003 * continuum, 0.004 * continuum, 0.5],
            DeltaLimit = [10.0, 25.0, 25.0, 0.1, 30.0, 500.0, 1e5, 0.5 * continuum, 0.5 * continuum, 0.25]
        };
    }

    private void Validate(InversionRequest request)
    {
        var full = request.Filters.GetLength(0);
        var bins = request.Filters.GetLength(1);
        if (bins is not (5 or 6 or 8 or 10)) throw new ArgumentException("Filter count must be 5, 6, 8, or 10.");
        if (full < _options.SyntheticWavelengthCount || (full - _options.SyntheticWavelengthCount) % 2 != 0)
            throw new ArgumentException($"The full wavelength count must surround the {_options.SyntheticWavelengthCount}-point synthesis range symmetrically.");
        if (request.Observed.Length != bins * 4) throw new ArgumentException("Observed must contain four Stokes groups.");
        if (request.ScatteredLight.Length is not 0 && request.ScatteredLight.Length != bins * 4) throw new ArgumentException("ScatteredLight has the wrong length.");
        if (request.InitialGuess.Length != 10 || request.FreeParameters.Length != 10) throw new ArgumentException("VFISV uses ten model parameters.");
        if (request.FreeParameters.Any(x => x is not (0 or 1))) throw new ArgumentException("FreeParameters values must be zero or one.");
        if (request.FreeParameters.Sum() == 0 || bins * 4 <= request.FreeParameters.Sum()) throw new ArgumentException("Invalid number of free parameters.");
    }

    private static (double[,] Inner, double[] OuterIntegral) SplitFilters(double[,] full, int innerCount)
    {
        var fullCount = full.GetLength(0); var bins = full.GetLength(1); var offset = (fullCount - innerCount) / 2;
        var inner = new double[innerCount, bins]; var outer = new double[bins];
        for (var b = 0; b < bins; b++)
        {
            for (var w = 0; w < innerCount; w++) inner[w, b] = full[offset + w, b];
            for (var w = 0; w < offset; w++) outer[b] += full[w, b] + full[fullCount - 1 - w, b];
        }
        return (inner, outer);
    }

    private static double[,] UnpackAndReverse(double[] values, int bins)
    {
        var result = new double[bins, 4];
        for (var s = 0; s < 4; s++) for (var b = 0; b < bins; b++) result[bins - 1 - b, s] = values[s * bins + b];
        return result;
    }

    private static double[,] Unpack(double[] values, int bins)
    {
        var result = new double[bins, 4];
        for (var s = 0; s < 4; s++) for (var b = 0; b < bins; b++) result[b, s] = values[s * bins + b];
        return result;
    }

    private static double[] Pack(double[,] values)
    {
        var bins = values.GetLength(0); var result = new double[bins * 4];
        for (var s = 0; s < 4; s++) for (var b = 0; b < bins; b++) result[s * bins + b] = values[b, s];
        return result;
    }

    private static InversionResult Failed(ConvergenceStatus status, int iterations, int bins) => new()
    {
        Model = Enumerable.Repeat(double.NaN, 10).ToArray(), Errors = Enumerable.Repeat(double.NaN, 12).ToArray(),
        SyntheticStokes = Enumerable.Repeat(double.NaN, bins * 4).ToArray(), Status = status,
        Iterations = iterations, ChiSquared = double.NaN
    };

    private static void WeakFieldGuess(double[,] observed, double[] guess, InversionContext context, VfisvOptions options)
    {
        var bins = context.BinCount; var maximumI = Enumerable.Range(0, bins).Max(i => observed[i, 0]);
        var absorption = new double[bins];
        for (var i = 0; i < bins; i++) absorption[i] = maximumI / context.Continuum - observed[i, 0] / context.Continuum;
        guess[0] = 5.0; guess[4] = 20.0;
        var sumU = 0.0; var sumQ = 0.0;
        for (var i = 0; i < bins; i++) { sumQ += observed[i, 1] / context.Continuum; sumU += observed[i, 2] / context.Continuum; }
        guess[2] = Math.Atan2(sumU, sumQ) * 90.0 / Math.PI;
        guess[7] = 0.15 * 0.98 * context.Continuum; guess[8] = 0.85 * 0.98 * context.Continuum;
        var numerator = 0.0; var denominator = 0.0;
        for (var i = 0; i < bins; i++) { numerator += absorption[i] * context.TunePositions[i]; denominator += absorption[i]; }
        var velocityPosition = numerator / denominator;
        var maximumV = Enumerable.Range(0, bins).Max(i => Math.Abs(observed[i, 3] / context.Continuum));
        if (maximumV > 2.0 * context.Noise[0])
        {
            numerator = denominator = 0.0;
            for (var i = 0; i < bins; i++) { var v = Math.Abs(observed[i, 3] / context.Continuum); numerator += v * context.TunePositions[i]; denominator += v; }
            velocityPosition = numerator / denominator;
        }
        guess[6] = 1e-3 * (2.998e10 / options.LineCenterAngstrom) * velocityPosition;
    }

    private static double ChiSquared(double[] model, double[,] synthetic, double[,] observed, double[] weights,
        InversionContext context, bool regularize)
    {
        var value = 0.0;
        for (var s = 0; s < 4; s++) for (var b = 0; b < context.BinCount; b++)
        { var d = observed[b, s] - synthetic[b, s]; value += weights[s] * weights[s] * d * d / context.FreeDegrees; }
        if (regularize && context.Free[0]) value += 0.002 * Math.Pow(model[0] - 5.0, 2.0);
        return value;
    }

    private static void ChangeDerivatives(double[] model, double[,,] derivatives, int bins)
    {
        for (var b = 0; b < bins; b++) for (var s = 0; s < 4; s++)
        {
            derivatives[0, b, s] -= 0.5 * model[4] / model[0] * derivatives[4, b, s];
            derivatives[4, b, s] /= Math.Sqrt(model[0]);
        }
    }

    private static void NormalizeAndMaskDerivatives(double[,,] derivatives, InversionContext context)
    {
        for (var p = 0; p < 10; p++) for (var b = 0; b < context.BinCount; b++) for (var s = 0; s < 4; s++)
            derivatives[p, b, s] = context.Free[p] ? derivatives[p, b, s] * context.Norm[p] : 0.0;
    }

    private static double[] Divergence(double[,] synthetic, double[,] observed, double[,,] derivatives,
        double[] weights, InversionContext context)
    {
        var result = new double[context.FreeLocations.Length];
        for (var i = 0; i < result.Length; i++) for (var s = 0; s < 4; s++) for (var b = 0; b < context.BinCount; b++)
            result[i] += 2.0 / context.FreeDegrees * weights[s] * weights[s]
                         * (observed[b, s] - synthetic[b, s]) * derivatives[context.FreeLocations[i], b, s];
        return result;
    }

    private static double[,] Hessian(double[,,] derivatives, double[] weights, InversionContext context)
    {
        var n = context.FreeLocations.Length; var result = new double[n, n];
        for (var i = 0; i < n; i++) for (var j = 0; j <= i; j++)
        {
            var sum = 0.0;
            for (var s = 0; s < 4; s++) for (var b = 0; b < context.BinCount; b++)
                sum += weights[s] * weights[s] * derivatives[context.FreeLocations[i], b, s] * derivatives[context.FreeLocations[j], b, s];
            result[i, j] = result[j, i] = 2.0 / context.FreeDegrees * sum;
        }
        return result;
    }

    private double[] ModelDelta(double[] model, double[] divergence, double[,] hessian, double lambda, InversionContext context)
    {
        var n = divergence.Length; var modified = (double[,])hessian.Clone(); var right = (double[])divergence.Clone();
        for (var i = 0; i < n; i++) modified[i, i] *= 1.0 + lambda;
        if (_options.UseRegularization && context.Free[0])
        {
            var etaIndex = Array.IndexOf(context.FreeLocations, 0);
            right[etaIndex] -= 0.004 * (model[0] - 5.0) * context.Norm[0];
            modified[etaIndex, etaIndex] += 0.004 * context.Norm[0] * context.Norm[0];
        }
        var result = new double[10];
        if (!SymmetricMatrix.TryPseudoInverseSolve(modified, right, _options.SvdTolerance, out var freeDelta)) return result;
        for (var i = 0; i < n; i++) if (double.IsFinite(freeDelta[i]) && Math.Abs(freeDelta[i]) <= 1e10) result[context.FreeLocations[i]] = freeDelta[i];
        return result;
    }

    private static (double[] Errors, bool Ok) CalculateErrors(double[,] hessian, double chi2, InversionContext context)
    {
        var output = new double[12];
        if (!SymmetricMatrix.TryPseudoInverse(hessian, 1e-32, out var inverse)) return (output, false);
        var covariance = new double[10, 10];
        for (var i = 0; i < context.FreeLocations.Length; i++) for (var j = 0; j < context.FreeLocations.Length; j++)
            covariance[context.FreeLocations[i], context.FreeLocations[j]] = chi2 / context.FreeDegrees * inverse[i, j]
                * context.Norm[context.FreeLocations[i]] * context.Norm[context.FreeLocations[j]];
        var sigma = new double[10];
        for (var i = 0; i < 10; i++) sigma[i] = Math.Sqrt(Math.Max(0.0, covariance[i, i]));
        output[0] = sigma[5]; output[1] = sigma[1]; output[2] = sigma[2]; output[3] = sigma[6]; output[4] = sigma[9];
        output[5] = Correlation(covariance, sigma, 5, 1); output[6] = Correlation(covariance, sigma, 5, 2);
        output[7] = Correlation(covariance, sigma, 1, 2); output[8] = Correlation(covariance, sigma, 5, 9);
        output[9] = Correlation(covariance, sigma, 1, 9); output[10] = Correlation(covariance, sigma, 2, 9);
        return (output, output.Take(11).All(double.IsFinite));
    }

    private static double Correlation(double[,] covariance, double[] sigma, int a, int b) =>
        sigma[a] == 0.0 || sigma[b] == 0.0 ? 0.0 : covariance[a, b] / sigma[a] / sigma[b];

    private static double NextLambda(double delta, double old) =>
        delta >= 1e-4 ? Math.Max(old / 5.0, 1e-4) : old * 10.0 * (old > 0.01 ? 2.0 : 1.0);

    private static void NormalizeDelta(double[] delta, InversionContext context)
    { for (var i = 0; i < 10; i++) delta[i] *= context.Norm[i]; }

    private static void UndoFiniteVariableChange(double[] model, double[] delta)
    {
        var eta = Math.Max(model[0] + delta[0], 0.001);
        var transformedWidth = model[4] * Math.Sqrt(model[0]) + delta[4];
        delta[4] = transformedWidth / Math.Sqrt(eta) - model[4];
    }

    private static void CutDelta(double[] delta, double[] model, InversionContext context)
    { for (var i = 0; i < 10; i++) delta[i] = Math.Clamp(delta[i], -context.DeltaLimit[i], context.DeltaLimit[i]); }

    private static void FineTune(double[] model, InversionContext context)
    {
        var s0 = model[7]; var sum = model[7] + model[8];
        model[7] = Math.Max(s0, context.LowerLimit[7]); model[8] = sum - model[7];
        for (var i = 0; i < 10; i++) if (context.Free[i] && i is not (1 or 2)) model[i] = Math.Clamp(model[i], context.LowerLimit[i], context.UpperLimit[i]);
        model[7] = Math.Max(s0, 0.15 * sum); model[8] = sum - model[7];
        model[1] %= 360.0; if (model[1] < 0.0) model[1] += 360.0; if (model[1] > 180.0) model[1] = 360.0 - model[1];
        model[2] %= 180.0; if (model[2] < 0.0) model[2] += 180.0;
    }

    private static void RandomModelJump(double[] model, InversionContext context, Random random)
    {
        var input = (double[])model.Clone();
        model[1] = 90.0 + 10.0 * Gaussian(random, 0.0, 1.0); model[0] = Gaussian(random, 5.0, 5.0);
        model[4] = Gaussian(random, 25.0, 10.0); model[5] = input[5] * Gaussian(random, 1.0, 0.5);
        model[2] = input[2] + Gaussian(random, 0.0, 45.0);
        var sourceSum = (input[7] + input[8]) * Gaussian(random, 1.0, 0.01);
        SetSourceRatio(model, sourceSum, 0.2 + 0.3 * random.NextDouble()); FineTune(model, context);
    }

    private static double Gaussian(Random random, double mean, double sigma)
    {
        var u1 = 1.0 - random.NextDouble(); var u2 = 1.0 - random.NextDouble();
        return mean + sigma * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    private static void SetSourceRatio(double[] model, double sum, double ratio)
    { model[7] = sum * ratio / (1.0 + ratio); model[8] = sum / (1.0 + ratio); }
}
