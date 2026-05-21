using Impressionist.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace Impressionist.Implementations
{
    public static class OctTreePaletteGenerator
    {
        public static Task<PaletteResult> CreatePalette(Dictionary<Vector3, int> sourceColor, int clusterCount, bool ignoreWhite = false)
        {
            var colorResult = KMeansPaletteGenerator.CreateThemeColor(sourceColor, ignoreWhite, true);
            return CreatePalette(sourceColor, clusterCount, colorResult, ignoreWhite);
        }

        public static Task<PaletteResult> CreatePalette(Dictionary<Vector3, int> sourceColor, int clusterCount, ThemeColorResult colorResult, bool ignoreWhite = false)
        {
            var quantizer = new PaletteQuantizer();
            if (sourceColor.Count == 1)
            {
                ignoreWhite = false;
            }
            var builder = sourceColor.AsEnumerable();
            if (ignoreWhite)
            {
                builder = builder.Where(t => t.Key.X <= 250 || t.Key.Y <= 250 || t.Key.Z <= 250);
            }
            if (colorResult.ColorIsDark)
            {
                builder = builder.Where(t => t.Key.PaletteRGBVectorLStarIsDark());
            }
            else
            {
                builder = builder.Where(t => t.Key.PaletteRGBVectorLStarIsLight());
            }
            var targetColor = builder.ToDictionary(t => t.Key, t => t.Value);
            foreach (var color in targetColor)
            {
                quantizer.AddColorRange(color.Key, color.Value);
            }
            quantizer.Quantize(clusterCount);
            var index = targetColor.Keys.ToList();
            List<Vector3> quantizeResult;
            quantizeResult = quantizer.GetPaletteResult(clusterCount);
            List<Vector3> result;
            if (quantizeResult.Count < clusterCount)
            {
                var count = quantizeResult.Count;
                result = new List<Vector3>();
                if (count > 0)
                {
                    for (int i = 0; i < clusterCount; i++)
                    {
                        result.Add(quantizeResult[i % count]);
                    }
                }
                else
                {
                    for (int i = 0; i < clusterCount; i++) result.Add(Vector3.Zero);
                }
            }
            else
            {
                result = quantizeResult;
            }
            return Task.FromResult(new PaletteResult(result, colorResult.ColorIsDark, colorResult));
        }
        internal sealed class PaletteQuantizer
        {
            private const int MaxDepth = 8;

            private readonly Node _root;
            private readonly List<Node>[] _levelNodes;

            public PaletteQuantizer()
            {
                _root = new Node(this, null, -1);
                _levelNodes = new List<Node>[MaxDepth];

                for (int i = 0; i < MaxDepth; i++)
                {
                    _levelNodes[i] = new List<Node>();
                }
            }

            public void AddColor(Vector3 color)
            {
                AddColorRange(color, 1);
            }

            public void AddColorRange(Vector3 color, int count)
            {
                if (count < 0)
                    throw new ArgumentOutOfRangeException(nameof(count));

                if (count == 0)
                    return;

                byte r = (byte)color.X;
                byte g = (byte)color.Y;
                byte b = (byte)color.Z;

                _root.AddColor(r, g, b, 0, count);
            }

            public void AddColor(byte r, byte g, byte b)
            {
                _root.AddColor(r, g, b, 0, 1);
            }

            public void AddColorRange(byte r, byte g, byte b, int count)
            {
                if (count < 0)
                    throw new ArgumentOutOfRangeException(nameof(count));

                if (count == 0)
                    return;

                _root.AddColor(r, g, b, 0, count);
            }

            internal void AddLevelNode(Node node, int level)
            {
                _levelNodes[level].Add(node);
            }

            public List<Vector3> GetPaletteResult()
            {
                if (_root.LeafCount == 0)
                    return new List<Vector3>();

                List<Vector3> result = new List<Vector3>(_root.LeafCount);
                _root.CollectColors(result);
                return result;
            }

            public List<Vector3> GetPaletteResult(int count)
            {
                if (count <= 0 || _root.LeafCount == 0)
                    return new List<Vector3>();

                List<PaletteEntry> entries = new List<PaletteEntry>(_root.LeafCount);
                _root.CollectEntries(entries);

                if (entries.Count <= count)
                {
                    List<Vector3> directResult = new List<Vector3>(entries.Count);

                    for (int i = 0; i < entries.Count; i++)
                    {
                        directResult.Add(entries[i].Color);
                    }

                    return directResult;
                }

                entries.Sort(ComparePaletteEntryByCountDescending);

                int take = Math.Min(count, entries.Count);
                List<Vector3> result = new List<Vector3>(take);

                for (int i = 0; i < take; i++)
                {
                    result.Add(entries[i].Color);
                }

                return result;
            }

            public void Quantize(int colorCount)
            {
                if (colorCount <= 0)
                    throw new ArgumentOutOfRangeException(nameof(colorCount));

                int nodesToRemove = _root.LeafCount - colorCount;

                if (nodesToRemove <= 0)
                    return;

                for (int level = MaxDepth - 2; level >= 0 && nodesToRemove > 0; level--)
                {
                    List<Node> nodes = _levelNodes[level];

                    nodes.Sort(CompareNodeForMerge);

                    for (int i = 0; i < nodes.Count && nodesToRemove > 0; i++)
                    {
                        Node node = nodes[i];

                        if (node.ChildrenCount == 0)
                            continue;

                        int reduction = node.LeafCount - 1;

                        if (reduction <= 0)
                            continue;

                        if (reduction > nodesToRemove)
                            continue;

                        nodesToRemove -= reduction;
                        node.Merge();
                    }
                }

                // 兜底：如果没有刚好可合并的节点，继续找最小可合并节点。
                // 这样可以保证最终颜色数量不超过 colorCount。
                while (_root.LeafCount > colorCount)
                {
                    Node node = FindSmallestReducibleNode();

                    if (node == null)
                        break;

                    node.Merge();
                }
            }

            private Node FindSmallestReducibleNode()
            {
                Node best = null;
                int bestReduction = int.MaxValue;
                long bestCount = long.MaxValue;

                for (int level = MaxDepth - 2; level >= 0; level--)
                {
                    List<Node> nodes = _levelNodes[level];

                    for (int i = 0; i < nodes.Count; i++)
                    {
                        Node node = nodes[i];

                        if (!node.IsAttachedToRoot())
                            continue;

                        if (node.ChildrenCount == 0)
                            continue;

                        int reduction = node.LeafCount - 1;

                        if (reduction <= 0)
                            continue;

                        if (reduction < bestReduction ||
                            reduction == bestReduction && node.TotalCount < bestCount)
                        {
                            best = node;
                            bestReduction = reduction;
                            bestCount = node.TotalCount;
                        }
                    }
                }

                return best;
            }

            private static int CompareNodeForMerge(Node a, Node b)
            {
                int leafCompare = a.LeafCount.CompareTo(b.LeafCount);

                if (leafCompare != 0)
                    return leafCompare;

                return a.TotalCount.CompareTo(b.TotalCount);
            }

            private static int ComparePaletteEntryByCountDescending(PaletteEntry a, PaletteEntry b)
            {
                int countCompare = b.Count.CompareTo(a.Count);

                if (countCompare != 0)
                    return countCompare;

                int xCompare = a.Color.X.CompareTo(b.Color.X);

                if (xCompare != 0)
                    return xCompare;

                int yCompare = a.Color.Y.CompareTo(b.Color.Y);

                if (yCompare != 0)
                    return yCompare;

                return a.Color.Z.CompareTo(b.Color.Z);
            }

            internal struct PaletteEntry
            {
                public readonly Vector3 Color;
                public readonly long Count;

                public PaletteEntry(Vector3 color, long count)
                {
                    Color = color;
                    Count = count;
                }
            }

            internal sealed class Node
            {
                private readonly PaletteQuantizer _owner;
                private readonly Node _parent;
                private readonly int _indexInParent;

                private Node _c0;
                private Node _c1;
                private Node _c2;
                private Node _c3;
                private Node _c4;
                private Node _c5;
                private Node _c6;
                private Node _c7;

                private int _childrenCount;
                private int _leafCount;

                private long _totalCount;
                private long _sumR;
                private long _sumG;
                private long _sumB;

                public Node(PaletteQuantizer owner, Node parent, int indexInParent)
                {
                    _owner = owner;
                    _parent = parent;
                    _indexInParent = indexInParent;
                }

                public int ChildrenCount
                {
                    get { return _childrenCount; }
                }

                public int LeafCount
                {
                    get { return _leafCount; }
                }

                public long TotalCount
                {
                    get { return _totalCount; }
                }

                private Vector3 AverageColor
                {
                    get
                    {
                        if (_totalCount == 0)
                            return Vector3.Zero;

                        return new Vector3(
                            (float)_sumR / _totalCount,
                            (float)_sumG / _totalCount,
                            (float)_sumB / _totalCount);
                    }
                }

                public void AddColor(byte r, byte g, byte b, int level, int count)
                {
                    _totalCount += count;
                    _sumR += (long)r * count;
                    _sumG += (long)g * count;
                    _sumB += (long)b * count;

                    if (level == MaxDepth)
                    {
                        if (_leafCount == 0)
                            _leafCount = 1;

                        return;
                    }

                    int index = GetIndex(r, g, b, level);
                    Node child = GetChild(index);

                    if (child == null)
                    {
                        child = new Node(_owner, this, index);
                        SetChild(index, child);
                        _childrenCount++;

                        _owner.AddLevelNode(child, level);
                    }

                    int oldLeafCount = child._leafCount;

                    child.AddColor(r, g, b, level + 1, count);

                    _leafCount += child._leafCount - oldLeafCount;
                }

                public void CollectColors(List<Vector3> result)
                {
                    if (_leafCount == 0)
                        return;

                    if (_childrenCount == 0)
                    {
                        result.Add(AverageColor);
                        return;
                    }

                    if (_c0 != null) _c0.CollectColors(result);
                    if (_c1 != null) _c1.CollectColors(result);
                    if (_c2 != null) _c2.CollectColors(result);
                    if (_c3 != null) _c3.CollectColors(result);
                    if (_c4 != null) _c4.CollectColors(result);
                    if (_c5 != null) _c5.CollectColors(result);
                    if (_c6 != null) _c6.CollectColors(result);
                    if (_c7 != null) _c7.CollectColors(result);
                }

                public void CollectEntries(List<PaletteEntry> result)
                {
                    if (_leafCount == 0)
                        return;

                    if (_childrenCount == 0)
                    {
                        result.Add(new PaletteEntry(AverageColor, _totalCount));
                        return;
                    }

                    if (_c0 != null) _c0.CollectEntries(result);
                    if (_c1 != null) _c1.CollectEntries(result);
                    if (_c2 != null) _c2.CollectEntries(result);
                    if (_c3 != null) _c3.CollectEntries(result);
                    if (_c4 != null) _c4.CollectEntries(result);
                    if (_c5 != null) _c5.CollectEntries(result);
                    if (_c6 != null) _c6.CollectEntries(result);
                    if (_c7 != null) _c7.CollectEntries(result);
                }

                public void Merge()
                {
                    if (_childrenCount == 0 || _leafCount <= 1)
                        return;

                    int oldLeafCount = _leafCount;

                    _c0 = null;
                    _c1 = null;
                    _c2 = null;
                    _c3 = null;
                    _c4 = null;
                    _c5 = null;
                    _c6 = null;
                    _c7 = null;

                    _childrenCount = 0;
                    _leafCount = _totalCount > 0 ? 1 : 0;

                    int reduction = oldLeafCount - _leafCount;

                    Node parent = _parent;

                    while (parent != null)
                    {
                        parent._leafCount -= reduction;
                        parent = parent._parent;
                    }
                }

                public bool IsAttachedToRoot()
                {
                    Node current = this;

                    while (current._parent != null)
                    {
                        if (!object.ReferenceEquals(
                            current._parent.GetChild(current._indexInParent),
                            current))
                        {
                            return false;
                        }

                        current = current._parent;
                    }

                    return true;
                }

                private Node GetChild(int index)
                {
                    switch (index)
                    {
                        case 0:
                            return _c0;
                        case 1:
                            return _c1;
                        case 2:
                            return _c2;
                        case 3:
                            return _c3;
                        case 4:
                            return _c4;
                        case 5:
                            return _c5;
                        case 6:
                            return _c6;
                        case 7:
                            return _c7;
                        default:
                            return null;
                    }
                }

                private void SetChild(int index, Node node)
                {
                    switch (index)
                    {
                        case 0:
                            _c0 = node;
                            break;
                        case 1:
                            _c1 = node;
                            break;
                        case 2:
                            _c2 = node;
                            break;
                        case 3:
                            _c3 = node;
                            break;
                        case 4:
                            _c4 = node;
                            break;
                        case 5:
                            _c5 = node;
                            break;
                        case 6:
                            _c6 = node;
                            break;
                        case 7:
                            _c7 = node;
                            break;
                    }
                }

                private static int GetIndex(byte r, byte g, byte b, int level)
                {
                    int shift = 7 - level;

                    return (((r >> shift) & 1) << 2)
                         | (((g >> shift) & 1) << 1)
                         | ((b >> shift) & 1);
                }
            }
        }
    }
}