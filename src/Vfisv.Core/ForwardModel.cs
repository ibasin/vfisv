namespace Vfisv;

internal static class ForwardModel
{
    private const double DegreesToRadians = Math.PI / 180.0;
    private const double LightSpeedCmPerSecond = 2.99792458e10;

    private sealed record Absorption(
        double[] EtaI, double[] EtaQ, double[] EtaU, double[] EtaV,
        double[] RhoQ, double[] RhoU, double[] RhoV,
        double[,] DEtaI, double[,] DEtaQ, double[,] DEtaU, double[,] DEtaV,
        double[,] DRhoQ, double[,] DRhoU, double[,] DRhoV);

    public static SynthesisResult Synthesize(
        double[] model,
        double[,] scattered,
        bool derivative,
        double[,] filters,
        double[] integratedFilters,
        InversionContext context,
        VfisvOptions options)
    {
        var nw = context.WavelengthCount;
        var bins = context.BinCount;
        var absorption = CalculateAbsorption(model, derivative, context, options);
        var spectral = new double[nw, 4];
        var spectralDerivatives = new double[9, nw, 4];
        var magnetic = new double[bins, 4];
        var magneticDerivatives = new double[9, bins, 4];
        var result = new double[bins, 4];
        var resultDerivatives = new double[10, bins, 4];
        var s0 = model[(int)ModelParameter.SourceContinuum];
        var s1 = model[(int)ModelParameter.SourceGradient];
        var alpha = model[(int)ModelParameter.MagneticFillingFactor];

        for (var w = 0; w < nw; w++)
        {
            var ei = absorption.EtaI[w];
            var eq = absorption.EtaQ[w];
            var eu = absorption.EtaU[w];
            var ev = absorption.EtaV[w];
            var rq = absorption.RhoQ[w];
            var ru = absorption.RhoU[w];
            var rv = absorption.RhoV[w];
            var extra = eq * rq + eu * ru + ev * rv;
            var det = ei * ei * (ei * ei - eq * eq - eu * eu - ev * ev + rq * rq + ru * ru + rv * rv)
                      - extra * extra;
            var ni = ei * (ei * ei + rq * rq + ru * ru + rv * rv);
            var nq = ei * ei * eq + ei * (ev * ru - eu * rv) + rq * extra;
            var nu = ei * ei * eu + ei * (eq * rv - ev * rq) + ru * extra;
            var nv = ei * ei * ev + ei * (eu * rq - eq * ru) + rv * extra;

            spectral[w, 0] = s0 + s1 * ni / det;
            spectral[w, 1] = -s1 * nq / det;
            spectral[w, 2] = -s1 * nu / det;
            spectral[w, 3] = -s1 * nv / det;

            if (!derivative) continue;
            for (var p = 0; p < 7; p++)
            {
                var dei = absorption.DEtaI[p, w];
                var deq = absorption.DEtaQ[p, w];
                var deu = absorption.DEtaU[p, w];
                var dev = absorption.DEtaV[p, w];
                var drq = absorption.DRhoQ[p, w];
                var dru = absorption.DRhoU[p, w];
                var drv = absorption.DRhoV[p, w];
                var dextra = deq * rq + eq * drq + deu * ru + eu * dru + dev * rv + ev * drv;
                var inner = ei * ei - eq * eq - eu * eu - ev * ev + rq * rq + ru * ru + rv * rv;
                var dinner = 2.0 * (ei * dei - eq * deq - eu * deu - ev * dev + rq * drq + ru * dru + rv * drv);
                var ddet = 2.0 * ei * dei * inner + ei * ei * dinner - 2.0 * extra * dextra;
                var dni = dei * (ei * ei + rq * rq + ru * ru + rv * rv)
                          + 2.0 * ei * (ei * dei + rq * drq + ru * dru + rv * drv);
                var dnq = 2.0 * ei * dei * eq + ei * ei * deq
                          + dei * (ev * ru - eu * rv)
                          + ei * (dev * ru + ev * dru - deu * rv - eu * drv)
                          + drq * extra + rq * dextra;
                var dnu = 2.0 * ei * dei * eu + ei * ei * deu
                          + dei * (eq * rv - ev * rq)
                          + ei * (deq * rv + eq * drv - dev * rq - ev * drq)
                          + dru * extra + ru * dextra;
                var dnv = 2.0 * ei * dei * ev + ei * ei * dev
                          + dei * (eu * rq - eq * ru)
                          + ei * (deu * rq + eu * drq - deq * ru - eq * dru)
                          + drv * extra + rv * dextra;

                spectralDerivatives[p, w, 0] = s1 * (dni / det - ni * ddet / (det * det));
                spectralDerivatives[p, w, 1] = s1 * (nq * ddet / (det * det) - dnq / det);
                spectralDerivatives[p, w, 2] = s1 * (nu * ddet / (det * det) - dnu / det);
                spectralDerivatives[p, w, 3] = s1 * (nv * ddet / (det * det) - dnv / det);
            }

            spectralDerivatives[7, w, 0] = 1.0;
            spectralDerivatives[8, w, 0] = (spectral[w, 0] - s0) / s1;
            for (var stokes = 1; stokes < 4; stokes++)
                spectralDerivatives[8, w, stokes] = spectral[w, stokes] / s1;
        }

        for (var bin = 0; bin < bins; bin++)
        for (var stokes = 0; stokes < 4; stokes++)
        {
            var sum = 0.0;
            for (var w = 0; w < nw; w++) sum += filters[w, bin] * spectral[w, stokes];
            magnetic[bin, stokes] = sum;
        }
        for (var bin = 0; bin < bins; bin++) magnetic[bin, 0] += integratedFilters[bin] * (s0 + s1);

        for (var bin = 0; bin < bins; bin++)
        for (var stokes = 0; stokes < 4; stokes++)
            result[bin, stokes] = (1.0 - alpha) * scattered[bin, stokes] + alpha * magnetic[bin, stokes];

        if (derivative)
        {
            for (var p = 0; p < 9; p++)
            for (var bin = 0; bin < bins; bin++)
            for (var stokes = 0; stokes < 4; stokes++)
            {
                var sum = 0.0;
                for (var w = 0; w < nw; w++) sum += filters[w, bin] * spectralDerivatives[p, w, stokes];
                magneticDerivatives[p, bin, stokes] = sum;
            }
            for (var bin = 0; bin < bins; bin++)
            {
                magneticDerivatives[7, bin, 0] += integratedFilters[bin];
                magneticDerivatives[8, bin, 0] += integratedFilters[bin];
            }
            for (var p = 0; p < 9; p++)
            for (var bin = 0; bin < bins; bin++)
            for (var stokes = 0; stokes < 4; stokes++)
                resultDerivatives[p, bin, stokes] = alpha * magneticDerivatives[p, bin, stokes];
            for (var bin = 0; bin < bins; bin++)
            for (var stokes = 0; stokes < 4; stokes++)
                resultDerivatives[9, bin, stokes] = magnetic[bin, stokes] - scattered[bin, stokes];
        }

        return new SynthesisResult(result, resultDerivatives);
    }

    private static Absorption CalculateAbsorption(double[] model, bool derivative,
        InversionContext context, VfisvOptions options)
    {
        var n = context.WavelengthCount;
        var etaI = new double[n]; var etaQ = new double[n]; var etaU = new double[n]; var etaV = new double[n];
        var rhoQ = new double[n]; var rhoU = new double[n]; var rhoV = new double[n];
        var dEtaI = new double[7, n]; var dEtaQ = new double[7, n]; var dEtaU = new double[7, n]; var dEtaV = new double[7, n];
        var dRhoQ = new double[7, n]; var dRhoU = new double[7, n]; var dRhoV = new double[7, n];

        var eta0 = model[0]; var gamma = model[1] * DegreesToRadians; var phi = model[2] * DegreesToRadians;
        var damping = model[3]; var doppler = model[4]; var field = model[5]; var velocity = model[6];
        var sinGamma = Math.Sin(gamma); var cosGamma = Math.Cos(gamma); var sin2Inc = sinGamma * sinGamma;
        var sinCosInc = sinGamma * cosGamma; var cos2Azi = Math.Cos(2.0 * phi); var sin2Azi = Math.Sin(2.0 * phi);

        for (var w = 0; w < n; w++)
        {
            var velocityShift = 1e3 * velocity * options.LineCenterAngstrom / LightSpeedCmPerSecond;
            var redFrequency = (context.Wave[w] - velocityShift + field * options.ZeemanShiftMilliAngstromPerGauss) / doppler;
            var blueFrequency = (context.Wave[w] - velocityShift - field * options.ZeemanShiftMilliAngstromPerGauss) / doppler;
            var piFrequency = (context.Wave[w] - velocityShift) / doppler;
            var (phiR, psiR) = VoigtProfile.Evaluate(damping, redFrequency);
            var (phiB, psiB) = VoigtProfile.Evaluate(damping, blueFrequency);
            var (phiP, psiP) = VoigtProfile.Evaluate(damping, piFrequency);
            var absorption1 = phiP - 0.5 * (phiB + phiR);
            var dispersion1 = psiP - 0.5 * (psiB + psiR);
            var absorption3 = phiR - phiB;
            var dispersion3 = psiR - psiB;

            etaI[w] = 1.0 + 0.5 * eta0 * (phiP * sin2Inc + 0.5 * (phiR + phiB) * (2.0 - sin2Inc));
            etaQ[w] = 0.5 * eta0 * absorption1 * sin2Inc * cos2Azi;
            etaU[w] = 0.5 * eta0 * absorption1 * sin2Inc * sin2Azi;
            etaV[w] = -0.5 * eta0 * absorption3 * cosGamma;
            rhoQ[w] = 0.5 * eta0 * dispersion1 * sin2Inc * cos2Azi;
            rhoU[w] = 0.5 * eta0 * dispersion1 * sin2Inc * sin2Azi;
            rhoV[w] = -0.5 * eta0 * dispersion3 * cosGamma;

            if (!derivative) continue;
            var rootPi = Math.Sqrt(Math.PI);
            var dPhiRDa = -2.0 / rootPi + 2.0 * (damping * phiR + redFrequency * psiR);
            var dPhiRdV = 2.0 * damping * psiR - 2.0 * redFrequency * phiR;
            var dPsiRDa = dPhiRdV; var dPsiRdV = -dPhiRDa;
            var dPhiBDa = -2.0 / rootPi + 2.0 * (damping * phiB + blueFrequency * psiB);
            var dPhiBdV = 2.0 * damping * psiB - 2.0 * blueFrequency * phiB;
            var dPsiBDa = dPhiBdV; var dPsiBdV = -dPhiBDa;
            var dPhiPDa = -2.0 / rootPi + 2.0 * (damping * phiP + piFrequency * psiP);
            var dPhiPdV = 2.0 * damping * psiP - 2.0 * piFrequency * phiP;
            var dPsiPDa = dPhiPdV; var dPsiPdV = -dPhiPDa;
            var dFreqVelocity = -1e3 * options.LineCenterAngstrom / (LightSpeedCmPerSecond * doppler);
            var dRedDoppler = -redFrequency / doppler; var dBlueDoppler = -blueFrequency / doppler; var dPiDoppler = -piFrequency / doppler;
            var dRedField = options.ZeemanShiftMilliAngstromPerGauss / doppler;
            var dBlueField = -dRedField;

            dEtaI[0, w] = (etaI[w] - 1.0) / eta0;
            dEtaI[1, w] = eta0 * sinCosInc * absorption1 * DegreesToRadians;
            dEtaI[3, w] = 0.5 * eta0 * (dPhiPDa * sin2Inc + 0.5 * (dPhiBDa + dPhiRDa) * (2.0 - sin2Inc));
            dEtaI[4, w] = 0.5 * eta0 * (dPhiPdV * dPiDoppler * sin2Inc + 0.5 * (dPhiBdV * dBlueDoppler + dPhiRdV * dRedDoppler) * (2.0 - sin2Inc));
            dEtaI[5, w] = 0.25 * eta0 * (dPhiBdV * dBlueField + dPhiRdV * dRedField) * (2.0 - sin2Inc);
            dEtaI[6, w] = 0.5 * eta0 * (dPhiPdV * dFreqVelocity * sin2Inc + 0.5 * (dPhiBdV + dPhiRdV) * dFreqVelocity * (2.0 - sin2Inc));

            FillLinearPolarizationDerivatives(dEtaQ, w, etaQ[w], eta0, absorption1, sin2Inc, sinCosInc, cos2Azi, sin2Azi,
                dPhiPDa, dPhiBDa, dPhiRDa, dPhiPdV, dPhiBdV, dPhiRdV,
                dPiDoppler, dBlueDoppler, dRedDoppler, dBlueField, dRedField, dFreqVelocity, false);
            FillLinearPolarizationDerivatives(dEtaU, w, etaU[w], eta0, absorption1, sin2Inc, sinCosInc, sin2Azi, cos2Azi,
                dPhiPDa, dPhiBDa, dPhiRDa, dPhiPdV, dPhiBdV, dPhiRdV,
                dPiDoppler, dBlueDoppler, dRedDoppler, dBlueField, dRedField, dFreqVelocity, true);

            dEtaV[0, w] = etaV[w] / eta0; dEtaV[1, w] = 0.5 * eta0 * absorption3 * sinGamma * DegreesToRadians;
            dEtaV[3, w] = -0.5 * eta0 * (dPhiRDa - dPhiBDa) * cosGamma;
            dEtaV[4, w] = -0.5 * eta0 * (dPhiRdV * dRedDoppler - dPhiBdV * dBlueDoppler) * cosGamma;
            dEtaV[5, w] = -0.5 * eta0 * (dPhiRdV * dRedField - dPhiBdV * dBlueField) * cosGamma;
            dEtaV[6, w] = -0.5 * eta0 * (dPhiRdV - dPhiBdV) * dFreqVelocity * cosGamma;

            FillLinearPolarizationDerivatives(dRhoQ, w, rhoQ[w], eta0, dispersion1, sin2Inc, sinCosInc, cos2Azi, sin2Azi,
                dPsiPDa, dPsiBDa, dPsiRDa, dPsiPdV, dPsiBdV, dPsiRdV,
                dPiDoppler, dBlueDoppler, dRedDoppler, dBlueField, dRedField, dFreqVelocity, false);
            FillLinearPolarizationDerivatives(dRhoU, w, rhoU[w], eta0, dispersion1, sin2Inc, sinCosInc, sin2Azi, cos2Azi,
                dPsiPDa, dPsiBDa, dPsiRDa, dPsiPdV, dPsiBdV, dPsiRdV,
                dPiDoppler, dBlueDoppler, dRedDoppler, dBlueField, dRedField, dFreqVelocity, true);

            dRhoV[0, w] = rhoV[w] / eta0; dRhoV[1, w] = 0.5 * eta0 * dispersion3 * sinGamma * DegreesToRadians;
            dRhoV[3, w] = -0.5 * eta0 * (dPsiRDa - dPsiBDa) * cosGamma;
            dRhoV[4, w] = -0.5 * eta0 * (dPsiRdV * dRedDoppler - dPsiBdV * dBlueDoppler) * cosGamma;
            dRhoV[5, w] = -0.5 * eta0 * (dPsiRdV * dRedField - dPsiBdV * dBlueField) * cosGamma;
            dRhoV[6, w] = -0.5 * eta0 * (dPsiRdV - dPsiBdV) * dFreqVelocity * cosGamma;
        }

        return new Absorption(etaI, etaQ, etaU, etaV, rhoQ, rhoU, rhoV,
            dEtaI, dEtaQ, dEtaU, dEtaV, dRhoQ, dRhoU, dRhoV);
    }

    private static void FillLinearPolarizationDerivatives(double[,] target, int w, double value,
        double eta0, double profile, double sin2Inc, double sinCosInc, double angularFactor, double otherAngularFactor,
        double dPiDa, double dBlueDa, double dRedDa, double dPiDv, double dBlueDv, double dRedDv,
        double dPiDoppler, double dBlueDoppler, double dRedDoppler,
        double dBlueField, double dRedField, double dFrequencyVelocity, bool isU)
    {
        target[0, w] = value / eta0;
        target[1, w] = sinCosInc * angularFactor * eta0 * profile * DegreesToRadians;
        target[2, w] = (isU ? 1.0 : -1.0) * eta0 * profile * sin2Inc * otherAngularFactor * DegreesToRadians;
        target[3, w] = 0.5 * eta0 * (dPiDa - 0.5 * (dBlueDa + dRedDa)) * sin2Inc * angularFactor;
        target[4, w] = 0.5 * eta0 * (dPiDv * dPiDoppler - 0.5 * (dBlueDv * dBlueDoppler + dRedDv * dRedDoppler)) * sin2Inc * angularFactor;
        target[5, w] = -0.25 * eta0 * (dBlueDv * dBlueField + dRedDv * dRedField) * sin2Inc * angularFactor;
        target[6, w] = 0.5 * eta0 * (dPiDv - 0.5 * (dBlueDv + dRedDv)) * dFrequencyVelocity * sin2Inc * angularFactor;
    }
}
