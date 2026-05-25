using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace TM.Framework.Common.Helpers.Numerics
{
    public static class VectorMath
    {
        public static float DotProduct(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
        {
            if (a.Length != b.Length)
                throw new ArgumentException($"向量维度不匹配: a={a.Length} b={b.Length}");
            if (a.Length == 0) return 0f;

            int i = 0;
            int simdWidth = Vector<float>.Count;
            var acc = Vector<float>.Zero;

            for (; i <= a.Length - simdWidth; i += simdWidth)
            {
                var va = new Vector<float>(a.Slice(i, simdWidth));
                var vb = new Vector<float>(b.Slice(i, simdWidth));
                acc += va * vb;
            }

            float sum = 0f;
            for (int k = 0; k < simdWidth; k++) sum += acc[k];
            for (; i < a.Length; i++) sum += a[i] * b[i];
            return sum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float L2Norm(ReadOnlySpan<float> v)
        {
            return MathF.Sqrt(DotProduct(v, v));
        }

        public static bool L2NormalizeInPlace(Span<float> v)
        {
            float norm = L2Norm(v);
            if (norm <= 1e-12f) return false;
            float inv = 1f / norm;
            for (int i = 0; i < v.Length; i++) v[i] *= inv;
            return true;
        }

        public static float[] L2Normalize(ReadOnlySpan<float> v)
        {
            var r = new float[v.Length];
            v.CopyTo(r);
            L2NormalizeInPlace(r);
            return r;
        }

        public static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
        {
            float dot = DotProduct(a, b);
            float na = L2Norm(a);
            float nb = L2Norm(b);
            if (na <= 1e-12f || nb <= 1e-12f) return 0f;
            return dot / (na * nb);
        }

        public static void QuantizeInt8(ReadOnlySpan<float> v, Span<sbyte> q, out float scale)
        {
            if (v.Length != q.Length)
                throw new ArgumentException($"量化维度不匹配: v={v.Length} q={q.Length}");

            float maxAbs = 0f;
            for (int i = 0; i < v.Length; i++)
            {
                float abs = MathF.Abs(v[i]);
                if (abs > maxAbs) maxAbs = abs;
            }

            if (maxAbs <= 1e-12f)
            {
                scale = 0f;
                q.Clear();
                return;
            }

            scale = maxAbs / 127f;
            float inv = 1f / scale;
            for (int i = 0; i < v.Length; i++)
            {
                int r = (int)MathF.Round(v[i] * inv);
                if (r > 127) r = 127;
                else if (r < -127) r = -127;
                q[i] = (sbyte)r;
            }
        }

        public static void DequantizeInt8(ReadOnlySpan<sbyte> q, float scale, Span<float> v)
        {
            if (v.Length != q.Length)
                throw new ArgumentException($"反量化维度不匹配: v={v.Length} q={q.Length}");
            for (int i = 0; i < q.Length; i++) v[i] = q[i] * scale;
        }

        public static (sbyte[] Quantized, float Scale) QuantizeInt8(ReadOnlySpan<float> v)
        {
            var q = new sbyte[v.Length];
            QuantizeInt8(v, q, out var scale);
            return (q, scale);
        }

        public static float[] DequantizeInt8(ReadOnlySpan<sbyte> q, float scale)
        {
            var v = new float[q.Length];
            DequantizeInt8(q, scale, v);
            return v;
        }
    }
}
