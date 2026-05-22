using Impressionist.Helpers;
using Impressionist.Helpers.Hct;
using System;
using System.Collections.Generic;
using System.Text;

namespace Impressionist;

public class Score
{
    private struct ScoredHct(Hct hct, double score) : IComparable<ScoredHct>
    {
        public Hct Hct = hct;
        public double Score = score;

        public int CompareTo(ScoredHct other)
        {
            return Score.CompareTo(other.Score);
        }
    }

    private const double TargetChroma = 48.0; // A1 Chroma
    private const double WeightProportion = 0.7;
    private const double WeightChromaAbove = 0.3;
    private const double WeightChromaBelow = 0.1;
    private const double CutoffChroma = 5.0;
    private const double CutoffExcitedProportion = 0.01;

    public static List<ArgbColor> CalculateScore(
        Dictionary<ArgbColor, int> colorsToPopulation,
        int desired = 4,
        ArgbColor? fallbackColorARGB = null,
        bool filter = true
    )
    {
        fallbackColorARGB ??= new ArgbColor(0xff4285F4);

        // Get the HCT color for each Argb value, while finding the per hue count and
        // total count.
        List<Hct> colorsHct = [];
        var huePopulation = Enumerable.Repeat(0, 360).ToList();
        var populationSum = 0;
        foreach (var entry in colorsToPopulation)
        {
            var argb = entry.Key;
            var population = entry.Value;
            var hct = Hct.From(argb);
            colorsHct.Add(hct);
            var hue = (int)Math.Floor(hct.Hue);
            huePopulation[hue] += population;
            populationSum += population;
        }

        // Hues with more usage in neighboring 30 degree slice get a larger number.
        var hueExcitedProportions = Enumerable.Repeat(0.0, 360).ToList();
        for (var hue = 0; hue < 360; hue++)
        {
            var proportion = huePopulation[hue] / (double)populationSum;
            for (var i = hue - 14; i < hue + 16; i++)
            {
                var neighborHue = (int)MathUtils.SanitizeDegrees(i);
                hueExcitedProportions[neighborHue] += proportion;
            }
        }

        // Scores each HCT color based on usage and chroma, while optionally
        // filtering out values that do not have enough chroma or usage.
        List<ScoredHct> scoredHcts = [];
        foreach (var hct in colorsHct)
        {
            var hue = (int)
                MathUtils.SanitizeDegrees(Math.Round(hct.Hue, MidpointRounding.AwayFromZero));
            var proportion = hueExcitedProportions[hue];
            if (filter && (hct.Chroma < CutoffChroma || proportion <= CutoffExcitedProportion))
                continue;

            var proportionScore = proportion * 100.0 * WeightProportion;
            var chromaWeight = hct.Chroma < TargetChroma ? WeightChromaBelow : WeightChromaAbove;
            var chromaScore = (hct.Chroma - TargetChroma) * chromaWeight;
            var score = proportionScore + chromaScore;
            scoredHcts.Add(new ScoredHct(hct, score));
        }

        // Sorted so that colors with higher scores come first.
        scoredHcts.Sort();
        scoredHcts.Reverse();

        // Iterates through potential hue differences in degrees in order to select
        // the colors with the largest distribution of hues possible. Starting at
        // 90 degrees(maximum difference for 4 colors) then decreasing down to a
        // 15 degree minimum.
        List<Hct> chosenColors = [];
        for (var differenceDegrees = 90; differenceDegrees >= 15; differenceDegrees--)
        {
            chosenColors.Clear();
            foreach (var entry in scoredHcts)
            {
                var hct = entry.Hct;
                var hasDuplicateHue = chosenColors.Any(chosenHct =>
                    MathUtils.DifferenceDegrees(hct.Hue, chosenHct.Hue) < differenceDegrees
                );
                if (!hasDuplicateHue)
                    chosenColors.Add(hct);

                if (chosenColors.Count >= desired)
                    break;
            }

            if (chosenColors.Count >= desired)
                break;
        }

        List<ArgbColor> colors = [];
        if (!chosenColors.Any())
            colors.Add(fallbackColorARGB.Value);

        colors.AddRange(chosenColors.Select(chosenHct => chosenHct.Argb));
        return colors;
    }
}