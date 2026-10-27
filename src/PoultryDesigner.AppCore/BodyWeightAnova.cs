using PoultryDesigner.Engine;

namespace PoultryDesigner.AppCore;

public sealed record BodyWeightAnovaResult(string Model, int Units, int TreatmentDf, int ErrorDf,
    double TreatmentSs, double ErrorSs, double? BlockSs, double? F, double? PValue, string Note)
{
    public string TargetStatus => PValue is null ? "Not estimable" : PValue > 0.6 ? "Meets target (p > 0.6)" : "Below target (p <= 0.6)";
}

// One observation per experimental unit, never one per animal. Blocked designs
// test treatment against the treatment-by-replicate residual (additive model).
public static class BodyWeightAnova
{
    public static BodyWeightAnovaResult Calculate(Study study, IEnumerable<PenStatistics> units)
    {
        var rows = units.ToArray(); int t = study.Treatments.Length, r = study.Replicates;
        if (t < 2 || r < 2 || rows.Length != t * r || rows.Select(p => (p.TreatmentIndex, p.Replicate)).Distinct().Count() != t * r
            || rows.Any(p => p.TreatmentIndex < 0 || p.TreatmentIndex >= t || p.Replicate < 1 || p.Replicate > r || p.Count != study.AnimalsPerPen))
            throw new ArgumentException("ANOVA requires a complete balanced treatment-by-replicate unit table.");
        bool blocked = study.Design != Design.Crd;
        string model = blocked ? "Treatment + replicate block" : "One-way treatment ANOVA";
        // Center in decimal before conversion so small BW differences survive a large common offset.
        decimal origin = rows[0].MeanWeightGrams;
        var values = rows.Select(p => (p.TreatmentIndex, p.Replicate, Y: p.MeanWeightGrams - origin)).ToArray();
        decimal grand = values.Average(p => p.Y);
        var treatment = values.GroupBy(p => p.TreatmentIndex).ToDictionary(g => g.Key, g => g.Average(p => p.Y));
        var blocks = values.GroupBy(p => p.Replicate).ToDictionary(g => g.Key, g => g.Average(p => p.Y));
        decimal Square(decimal x) => x * x;
        double treatmentSs = (double)(r * treatment.Values.Sum(x => Square(x - grand)));
        double? blockSs = blocked ? (double)(t * blocks.Values.Sum(x => Square(x - grand))) : null;
        double errorSs = (double)values.Sum(p => Square(p.Y - treatment[p.TreatmentIndex] - (blocked ? blocks[p.Replicate] - grand : 0)));
        int treatmentDf = t - 1, errorDf = blocked ? (t - 1) * (r - 1) : t * (r - 1);
        if (errorSs == 0) return new(model, rows.Length, treatmentDf, errorDf, treatmentSs, errorSs, blockSs, null,
            treatmentSs == 0 ? null : 0, treatmentSs == 0 ? "No residual or treatment variation; F and p are not estimable."
                : "Zero residual variance; F is infinite and the limiting p-value is 0.");
        double f = treatmentSs / treatmentDf / (errorSs / errorDf);
        double pValue = Survival(f, treatmentDf, errorDf);
        return new(model, rows.Length, treatmentDf, errorDf, treatmentSs, errorSs, blockSs, f, pValue,
            "Baseline balance diagnostic. A high p-value does not prove equal weights or a successful allocation.");
    }

    // F upper tail = I_x(dfError/2, dfTreatment/2), x = dfError/(dfError + dfTreatment*F).
    // Continued fraction and log-Gamma keep the tail stable without subtracting it from 1.
    internal static double Survival(double f, int numeratorDf, int denominatorDf)
    {
        if (f == 0) return 1;
        double x = denominatorDf / (denominatorDf + numeratorDf * f);
        return Beta(x, denominatorDf / 2d, numeratorDf / 2d);
    }
    private static double Beta(double x, double a, double b)
    {
        if (x <= 0) return 0; if (x >= 1) return 1;
        double scale = Math.Exp(LogGamma(a + b) - LogGamma(a) - LogGamma(b) + a * Math.Log(x) + b * Math.Log(1 - x));
        double result = x < (a + 1) / (a + b + 2) ? scale * Fraction(a, b, x) / a : 1 - scale * Fraction(b, a, 1 - x) / b;
        return Math.Clamp(result, 0, 1);
    }
    private static double Fraction(double a, double b, double x)
    {
        const double tiny = 1e-300;
        double Guard(double v) => Math.Abs(v) < tiny ? Math.CopySign(tiny, v) : v;
        double c = 1, d = 1 / Guard(1 - (a + b) * x / (a + 1)), h = d;
        for (int m = 1; m <= 10000; m++) {
            double aa = m * (b - m) * x / ((a + 2 * m - 1) * (a + 2 * m));
            d = 1 / Guard(1 + aa * d); c = Guard(1 + aa / c); h *= d * c;
            aa = -(a + m) * (a + b + m) * x / ((a + 2 * m) * (a + 2 * m + 1));
            d = 1 / Guard(1 + aa * d); c = Guard(1 + aa / c); double delta = d * c; h *= delta;
            if (Math.Abs(delta - 1) <= 3e-14) return h;
        }
        throw new InvalidOperationException("ANOVA p-value calculation did not converge.");
    }
    private static double LogGamma(double z)
    {
        double[] coefficients = [676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059,
            12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7];
        if (z < .5) return Math.Log(Math.PI) - Math.Log(Math.Sin(Math.PI * z)) - LogGamma(1 - z);
        z--; double sum = .99999999999980993;
        for (int i = 0; i < coefficients.Length; i++) sum += coefficients[i] / (z + i + 1);
        double v = z + 7.5;
        return .5 * Math.Log(2 * Math.PI) + (z + .5) * Math.Log(v) - v + Math.Log(sum);
    }
}
