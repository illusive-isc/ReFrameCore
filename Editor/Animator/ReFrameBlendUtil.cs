using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace jp.illusive_isc.ReFrame.Core.Editor
{
    /// <summary>BlendTree で、値が閾値/座標のどれとも一致しない「無段階 (連続値) の中間値」だった場合に、 実際の Unity の実行時ブレンドに倣って複数の子を重み付き合成するための共通処理。</summary>
    internal static class ReFrameBlendUtil
    {
        internal readonly struct WeightedFloats
        {
            internal readonly IReadOnlyDictionary<EditorCurveBinding, float> Values;
            internal readonly float Weight;

            internal WeightedFloats(IReadOnlyDictionary<EditorCurveBinding, float> values, float weight)
            {
                Values = values;
                Weight = weight;
            }
        }

        internal readonly struct WeightedObjects<T>
        {
            internal readonly IReadOnlyDictionary<EditorCurveBinding, T> Values;
            internal readonly float Weight;

            internal WeightedObjects(IReadOnlyDictionary<EditorCurveBinding, T> values, float weight)
            {
                Values = values;
                Weight = weight;
            }
        }

        /// <summary>ブレンド解決中に大量に生成される一時的な Dictionary&lt;EditorCurveBinding, ...&gt; を使い回すための プール。</summary>
        internal static class Pool
        {
            static readonly Stack<Dictionary<EditorCurveBinding, float>> FloatDicts = new();
            static readonly Stack<Dictionary<EditorCurveBinding, Object>> ObjectDicts = new();

            internal static Dictionary<EditorCurveBinding, float> RentFloats() =>
                FloatDicts.Count > 0 ? FloatDicts.Pop() : new Dictionary<EditorCurveBinding, float>();

            internal static void ReturnFloats(Dictionary<EditorCurveBinding, float> dict)
            {
                dict.Clear();
                FloatDicts.Push(dict);
            }

            internal static Dictionary<EditorCurveBinding, Object> RentObjects() =>
                ObjectDicts.Count > 0 ? ObjectDicts.Pop() : new Dictionary<EditorCurveBinding, Object>();

            internal static void ReturnObjects(Dictionary<EditorCurveBinding, Object> dict)
            {
                dict.Clear();
                ObjectDicts.Push(dict);
            }
        }

        internal static bool IsContinuousFloatBinding(EditorCurveBinding binding) =>
            binding.type == typeof(Transform) || binding.propertyName.StartsWith("blendShape.");

        /// <summary>float 値の集合を合成する。</summary>
        internal static void MergeWeightedFloats(
            IReadOnlyList<WeightedFloats> parts,
            Dictionary<EditorCurveBinding, float> outFloats
        )
        {
            var keys = new HashSet<EditorCurveBinding>();
            foreach (var part in parts)
                keys.UnionWith(part.Values.Keys);

            foreach (var key in keys)
            {
                var isContinuous = IsContinuousFloatBinding(key);
                var weightedSum = 0f;
                var weightTotal = 0f;
                var bestWeight = -1f;
                var bestValue = 0f;
                foreach (var part in parts)
                {
                    if (!part.Values.TryGetValue(key, out var v))
                        continue;
                    weightedSum += v * part.Weight;
                    weightTotal += part.Weight;
                    if (part.Weight > bestWeight)
                    {
                        bestWeight = part.Weight;
                        bestValue = v;
                    }
                }
                if (weightTotal <= 0f)
                    continue;
                outFloats[key] = isContinuous ? weightedSum / weightTotal : bestValue;
            }
        }

        /// <summary>連続値として意味を持たない値 (Object 参照であるマテリアル差し替えなど) の集合を合成する。</summary>
        internal static void MergeWeightedObjects<T>(
            IReadOnlyList<WeightedObjects<T>> parts,
            Dictionary<EditorCurveBinding, T> outValues
        )
        {
            var keys = new HashSet<EditorCurveBinding>();
            foreach (var part in parts)
                keys.UnionWith(part.Values.Keys);

            foreach (var key in keys)
            {
                var bestWeight = -1f;
                var bestValue = default(T);
                foreach (var part in parts)
                {
                    if (!part.Values.TryGetValue(key, out var v))
                        continue;
                    if (part.Weight > bestWeight)
                    {
                        bestWeight = part.Weight;
                        bestValue = v;
                    }
                }
                if (bestWeight >= 0f)
                    outValues[key] = bestValue;
            }
        }

        /// <summary>Simple1D の隣接 2 点 (lo/hi、重み 1-t / t) 用のショートハンド。</summary>
        internal static void MergeInterpolated(
            IReadOnlyDictionary<EditorCurveBinding, float> lo,
            IReadOnlyDictionary<EditorCurveBinding, float> hi,
            float t,
            Dictionary<EditorCurveBinding, float> outFloats
        )
        {
            MergeWeightedFloats(
                new[] { new WeightedFloats(lo, 1f - t), new WeightedFloats(hi, t) },
                outFloats
            );
        }

        /// <summary>Simple1D の隣接 2 点 (lo/hi、重み 1-t / t) 用のショートハンド。</summary>
        internal static void MergeDominant<T>(
            IReadOnlyDictionary<EditorCurveBinding, T> lo,
            IReadOnlyDictionary<EditorCurveBinding, T> hi,
            float t,
            Dictionary<EditorCurveBinding, T> outValues
        )
        {
            MergeWeightedObjects(
                new[] { new WeightedObjects<T>(lo, 1f - t), new WeightedObjects<T>(hi, t) },
                outValues
            );
        }

        /// <summary>Gradient Band Interpolation (M.</summary>
        internal static float[] ComputeGbiWeights(IReadOnlyList<Vector2> points, Vector2 target)
        {
            var n = points.Count;
            var weights = new float[n];
            if (n == 0)
                return weights;
            if (n == 1)
            {
                weights[0] = 1f;
                return weights;
            }

            for (var i = 0; i < n; i++)
            {
                var wi = float.MaxValue;
                for (var j = 0; j < n; j++)
                {
                    if (i == j)
                        continue;
                    var pij = points[j] - points[i];
                    var lenSq = pij.sqrMagnitude;
                    if (lenSq <= 1e-8f)
                        continue;
                    var a = Vector2.Dot(target - points[i], pij) / lenSq;
                    var f = 1f - a;
                    if (f < wi)
                        wi = f;
                }
                weights[i] = Mathf.Max(wi, 0f);
            }

            var sum = 0f;
            foreach (var w in weights)
                sum += w;
            if (sum <= 1e-8f)
            {

                var best = 0;
                var bestDist = float.MaxValue;
                for (var i = 0; i < n; i++)
                {
                    var d = (points[i] - target).sqrMagnitude;
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = i;
                    }
                }
                weights[best] = 1f;
                return weights;
            }

            for (var i = 0; i < n; i++)
                weights[i] /= sum;
            return weights;
        }
    }
}
