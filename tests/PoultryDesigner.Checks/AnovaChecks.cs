using System.Collections.Immutable;
using PoultryDesigner.AppCore;
using PoultryDesigner.Engine;

internal static class AnovaChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool condition, string message) { checks++; if (!condition) throw new Exception("ANOVA: " + message); }
        BodyWeightAnovaResult Calculate(decimal[][] weights, Design design, decimal offset = 0)
        {
            int t = weights.Length, r = weights[0].Length;
            var study = new Study(design, [], [], Enumerable.Range(0, t).Select(i => new Treatment(i.ToString(), "Treatment " + i)).ToImmutableArray(), r, 2);
            return BodyWeightAnova.Calculate(study, weights.SelectMany((values, tr) => values.Select((v, rep) => new PenStatistics(tr, rep + 1, 2, (v + offset) * 2, v + offset, null))));
        }
        bool Near(double? actual, double expected) => actual is { } value && Math.Abs(value - expected) <= Math.Max(1e-13, Math.Abs(expected) * 1e-11);
        var one = Calculate([[10, 12], [13, 15]], Design.Crd);
        Check(Near(one.TreatmentSs, 9) && Near(one.ErrorSs, 4) && Near(one.F, 4.5), "Hand-calculated one-way SS and F");
        Check(Near(one.PValue, 1 - Math.Sqrt(4.5 / 6.5)), "One-way F(1,2) upper tail matches closed-form probability");
        var block = Calculate([[10, 12], [13, 17]], Design.Rcbd);
        Check(Near(block.TreatmentSs, 16) && Near(block.BlockSs, 9) && Near(block.ErrorSs, 1) && Near(block.F, 16), "Hand-calculated blocked SS and F");
        Check(Near(block.PValue, 2 / Math.PI * Math.Atan(.25)), "Blocked F(1,1) upper tail matches closed-form probability");
        Check(Calculate([[10, 12], [13, 17]], Design.Conventional) == block, "Conventional BW-blocked allocation uses replicate-adjusted unit means");
        var equal = Calculate([[10, 12], [10, 12]], Design.Crd);
        Check(equal.PValue == 1 && equal.TargetStatus.StartsWith("Meets"), "Equal treatment means with residual variation give p=1");
        Check(Calculate([[10, 10], [10, 10]], Design.Crd).PValue is null, "All-identical weights do not produce a fabricated p-value");
        var zero = Calculate([[10, 10], [12, 12]], Design.Crd);
        Check(zero.PValue == 0 && zero.F is null && zero.Note.Contains("infinite"), "Distinct constant groups explicitly report infinite F and limiting p=0");
        Check((one with { PValue = .61 }).TargetStatus.StartsWith("Meets") && (one with { PValue = .6 }).TargetStatus.StartsWith("Below"), "Target uses strictly greater than 0.6, including 0.61");
        var shifted = Calculate([[10, 12], [13, 17]], Design.Rcbd, 999000);
        Check(Near(shifted.PValue, block.PValue!.Value), "Large common BW offset does not change ANOVA");
        foreach (int r in new[] { 2, 3, 8, 100 }) foreach (decimal scale in new[] { .000001m, 1m, 1000m })
        {
            var weights = Enumerable.Range(0, 3).Select(tr => Enumerable.Range(0, r).Select(rep => scale * (100m + tr / 7m + rep / 3m + (tr * rep % 5) / 11m)).ToArray()).ToArray();
            var result = Calculate(weights, Design.Crd);
            double expected = Math.Pow(result.ErrorDf / (result.ErrorDf + 2 * result.F!.Value), result.ErrorDf / 2d);
            Check(Near(result.PValue, expected), "F(2,df) survival agrees with independent closed form across scale and replication");
        }
        Console.WriteLine($"PASS: unit-mean treatment ANOVA ({checks} assertions)."); return checks;
    }
}
