using Impressionist.Abstractions;
using Impressionist.Implementations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace Impressionist.Shared.Implementations
{
    // I'm really appreciate wieslawsoltes's PaletteGenerator. Which make this project possible.
    public class KMeansPaletteGenerator :
        IThemeColorGenrator,
        IPaletteGenrator
    {
        public Task<ThemeColorResult> CreateThemeColor(Dictionary<Vector3, int> sourceColor, bool ignoreWhite = false)
        {
            var targetColor = sourceColor;
            if (ignoreWhite && sourceColor.Count > 1)
            {
                var hsvColor = sourceColor.ToDictionary(t => t.Key.RGBVectorToHSVColor(), t => t.Value);
                targetColor = hsvColor.Where(t => (t.Key.V != 100 || t.Key.S != 0)).ToDictionary(t => t.Key.HSVColorToRGBVector(), t => t.Value);
            }
            var clusters = KMeansCluster(sourceColor, 1);
            var colorVector = clusters.First().OrderByDescending(t => t.Value).First().Key;
            var isDark = colorVector.RGBVectorToHSVColor().V <= 50f;
            return Task.FromResult(new ThemeColorResult(colorVector, isDark));
        }

        public async Task<PaletteResult> CreatePalette(Dictionary<Vector3, int> sourceColor, int clusterCount, bool ignoreWhite = false)
        {
            if (sourceColor.Count == 1)
            {
                ignoreWhite = false;
            }
            var colorResult = await CreateThemeColor(sourceColor, ignoreWhite);
            var hsvColor = sourceColor.ToDictionary(t => t.Key.RGBVectorToHSVColor(), t => t.Value);
            var colorIsDark = colorResult.ColorIsDark;
            Dictionary<Vector3, int> targetColors = null;
            if (colorIsDark)
            {
                targetColors = hsvColor.Where(t => t.Key.V < 50)
                    .OrderByDescending(t => t.Value)
                    .ToDictionary(t => t.Key.HSVColorToRGBVector(), t => t.Value);
            }
            else
            {
                if (!ignoreWhite)
                {
                    targetColors = hsvColor.Where(t => t.Key.V >= 50)
                    .OrderByDescending(t => t.Value)
                    .ToDictionary(t => t.Key.HSVColorToRGBVector(), t => t.Value);
                }
                else
                {
                    targetColors = hsvColor.Where(t => t.Key.V >= 50 && (t.Key.V != 100 || t.Key.S != 0))
                    .OrderByDescending(t => t.Value)
                    .ToDictionary(t => t.Key.HSVColorToRGBVector(), t => t.Value);
                }
            }
            var clusters = KMeansCluster(targetColors, clusterCount);
            var dominantColors = new List<Vector3>();
            foreach (var cluster in clusters)
            {
                var representative = cluster.OrderByDescending(c => c.Value).First().Key;
                dominantColors.Add(representative);
            }
            var result = new List<Vector3>();
            var count = dominantColors.Count;
            for (int i = 0; i < clusterCount; i++)
            {
                // You know, it is always hard to fullfill a palette when you have no enough colors. So please forgive me when placing the same color over and over again.
                result.Add(dominantColors[i % count]);
            }
            return new PaletteResult(result, colorIsDark, colorResult);
        }
        static List<Dictionary<Vector3, int>> KMeansCluster(Dictionary<Vector3, int> colors, int numClusters)
        {
            // Initialize the clusters, reduces the total number when total colors is less than clusters
            var clusterCount = Math.Min(numClusters, colors.Count);
            var clusters = new List<Dictionary<Vector3, int>>();
            for (int i = 0; i < clusterCount; i++)
            {
                clusters.Add(new Dictionary<Vector3, int>());
            }

            // Select the initial cluster centers randomly
            var centers = colors.Keys.OrderByDescending(t => Guid.NewGuid()).Take(clusterCount).ToArray();
            // Loop until the clusters stabilize
            var changed = true;
            while (changed)
            {
                changed = false;
                // Assign each color to the nearest cluster center
                foreach (var color in colors.Keys)
                {
                    var nearest = FindNearestCenter(color, centers);
                    var clusterIndex = Array.IndexOf(centers, nearest);
                    clusters[clusterIndex][color] = colors[color];
                }

                // Recompute the cluster centers
                for (int i = 0; i < Math.Min(numClusters, clusterCount); i++)
                {
                    var sumR = 0f;
                    var sumG = 0f;
                    var sumB = 0f;
                    var count = 0f;
                    foreach (var color in clusters[i].Keys)
                    {
                        sumR += color.X;
                        sumG += color.Y;
                        sumB += color.Z;
                        count++;
                    }

                    var r = (sumR / count);
                    var g = (sumG / count);
                    var b = (sumB / count);
                    var newCenter = new Vector3(r, g, b);
                    if (!newCenter.Equals(centers[i]))
                    {
                        centers[i] = newCenter;
                        changed = true;
                    }
                }
            }

            // Return the clusters
            return clusters;
        }

        static Vector3 FindNearestCenter(Vector3 color, Vector3[] centers)
        {
            var nearest = centers[0];
            var minDist = float.MaxValue;

            foreach (var center in centers)
            {
                var dist = Vector3.Distance(color, center); // The original version implemented a Distance method by wieslawsoltes himself, I changed that to Vector ones.
                if (dist < minDist)
                {
                    nearest = center;
                    minDist = dist;
                }
            }

            return nearest;
        }
    }
}
