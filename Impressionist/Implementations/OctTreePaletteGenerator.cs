using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Impressionist.Abstractions;
using System.Numerics;

namespace Impressionist.Implementations
{
    public class OctTreePaletteGenerator:
        IThemeColorGenrator,
        IPaletteGenrator
    {
        public Task<ThemeColorResult> CreateThemeColor(Dictionary<Vector3, int> sourceColor, bool ignoreWhite = false)
        {
            var quantizer = new PaletteQuantizer();
            var builder = sourceColor.AsEnumerable();
            if (ignoreWhite && sourceColor.Count > 1)
            {
                builder = builder.Where(t => t.Key.X <= 250 || t.Key.Y <= 250 || t.Key.Z <= 250);
            }
            var targetColor = builder.ToDictionary(t => t.Key, t => t.Value);
            foreach (var color in targetColor)
            {
                for (var i = 0; i < color.Value; i++)
                {
                    quantizer.AddColor(color.Key);
                }
            }
            quantizer.Quantize(1);
            var index = new List<Vector3>() { targetColor.Keys.FirstOrDefault() };
            var result = quantizer.GetResult(index).FirstOrDefault();
            var colorIsDark = result.RGBVectorToHSVColor().V <= 50f;
            return Task.FromResult(new ThemeColorResult(result, colorIsDark));
        }
        public async Task<PaletteResult> CreatePalette(Dictionary<Vector3, int> sourceColor, int clusterCount, bool ignoreWhite = false)
        {
            var quantizer = new PaletteQuantizer();
            if (sourceColor.Count == 1)
            {
                ignoreWhite = false;
            }
            var colorResult = await CreateThemeColor(sourceColor, ignoreWhite);
            var builder = sourceColor.AsEnumerable();
            if (ignoreWhite)
            {
                builder = builder.Where(t => t.Key.X <= 250 || t.Key.Y <= 250 || t.Key.Z <= 250);
            }
            var colorIsDark = colorResult.ColorIsDark;
            if (colorIsDark)
            {
                builder = builder.Where(t => t.Key.RGBVectorToHSVColor().V < 65);
            }
            else
            {
                builder = builder.Where(t => t.Key.RGBVectorToHSVColor().V >= 65);
            }
            var targetColor = builder.ToDictionary(t=>t.Key, t=>t.Value);
            foreach (var color in targetColor)
            {
                for (var i = 0; i < color.Value; i++) 
                {
                    quantizer.AddColor(color.Key);
                }
            }
            quantizer.Quantize(clusterCount);
            var index = targetColor.Keys.ToList();
            var quantizeResult = quantizer.GetResult(index);
            var result = new List<Vector3>();
            var count = quantizeResult.Count;
            for (int i = 0; i < clusterCount; i++)
            {
                // You know, it is always hard to fullfill a palette when you have no enough colors. So please forgive me when placing the same color over and over again.
                result.Add(quantizeResult[i % count]);
            }
            return new PaletteResult(result, colorIsDark, colorResult);
        }

        private class PaletteQuantizer
        {
            private readonly Node Root;
            private IDictionary<int, List<Node>> levelNodes;

            public PaletteQuantizer()
            {
                Root = new Node(this);
                levelNodes = new Dictionary<int, List<Node>>();
                for (int i = 0; i < 8; i++)
                {
                    levelNodes[i] = new List<Node>();
                }
            }

            public void AddColor(Vector3 color)
            {
                Root.AddColor(color, 0);
            }

            public void AddLevelNode(Node node, int level)
            {
                levelNodes[level].Add(node);
            }

            public List<Vector3> GetResult(IEnumerable<Vector3> colors)
            {
                var result = new List<Vector3>();
                foreach (var item in colors)
                {
                    var color = Root.GetColor(item, 0);
                    if (!result.Contains(color))
                    {
                        result.Add(color);
                    }
                }
                return result;
            }

            public void Quantize(int colorCount)
            {
                var nodesToRemove = levelNodes[7].Count - colorCount;
                int level = 6;
                var toBreak = false;
                while (level >= 0 && nodesToRemove > 0)
                {
                    var leaves = levelNodes[level]
                        .Where(n => n.ChildrenCount - 1 <= nodesToRemove)
                        .OrderBy(n => n.ChildrenCount);
                    foreach (var leaf in leaves)
                    {
                        if (leaf.ChildrenCount > nodesToRemove)
                        {
                            toBreak = true;
                            continue;
                        }
                        nodesToRemove -= (leaf.ChildrenCount - 1);
                        leaf.Merge();
                        if (nodesToRemove <= 0)
                        {
                            break;
                        }
                    }
                    levelNodes.Remove(level + 1);
                    level--;
                    if (toBreak)
                    {
                        break;
                    }
                }
            }
        }

        private class Node
        {
            private readonly PaletteQuantizer parent;
            private Node[] Children = new Node[8];
            private Vector3 Color { get; set; }
            private int Count { get; set; }

            public int ChildrenCount => Children.Count(c => c != null);

            public Node(PaletteQuantizer parent)
            {
                this.parent = parent;
            }

            public void AddColor(Vector3 color, int level)
            {
                if (level < 8)
                {
                    var index = GetIndex(color, level);
                    if (Children[index] == null)
                    {
                        var newNode = new Node(parent);
                        Children[index] = newNode;
                        parent.AddLevelNode(newNode, level);
                    }
                    Children[index].AddColor(color, level + 1);
                }
                else
                {
                    Color = color;
                    Count++;
                }
            }

            public Vector3 GetColor(Vector3 color, int level)
            {
                if (ChildrenCount == 0)
                {
                    return Color;
                }
                else
                {
                    var index = GetIndex(color, level);
                    return Children[index].GetColor(color, level + 1);
                }
            }

            private byte GetIndex(Vector3 color, int level)
            {
                byte ret = 0;
                var mask = Convert.ToByte(0b10000000 >> level);
                if (((byte)color.X & mask) != 0)
                {
                    ret |= 0b100;
                }
                if (((byte)color.Y & mask) != 0)
                {
                    ret |= 0b010;
                }
                if (((byte)color.Z & mask) != 0)
                {
                    ret |= 0b001;
                }
                return ret;
            }

            public void Merge()
            {
                Color = Average(Children.Where(c => c != null).Select(c => new Tuple<Vector3, int>(c.Color, c.Count)));
                Count = Children.Sum(c => c?.Count ?? 0);
                Children = new Node[8];
            }

            private static Vector3 Average(IEnumerable<Tuple<Vector3, int>> colors)
            {
                var totals = colors.Sum(c => c.Item2);
                return new Vector3(
                    x: colors.Sum(c => c.Item1.X * c.Item2) / totals,
                    y: colors.Sum(c => c.Item1.Y * c.Item2) / totals,
                    z: colors.Sum(c => c.Item1.Z * c.Item2) / totals);
            }
        }
    }
}