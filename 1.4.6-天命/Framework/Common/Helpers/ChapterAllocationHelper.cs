using System;
using System.Collections.Generic;

namespace TM.Framework.Common.Helpers
{
    public static class ChapterAllocationHelper
    {
        private static readonly double[] FiveActWeights = { 2.0, 2.0, 3.0, 2.0, 1.0 };

        public static List<VolumeChapterRange> Allocate(int totalVolumes, int totalChapters)
        {
            if (totalVolumes <= 0) throw new ArgumentOutOfRangeException(nameof(totalVolumes), "总卷数必须大于0");
            if (totalChapters < totalVolumes) throw new ArgumentOutOfRangeException(nameof(totalChapters), "总章节数不能少于总卷数");

            var weights = BuildWeights(totalVolumes);
            var counts = DistributeChapters(weights, totalChapters);

            var result = new List<VolumeChapterRange>(totalVolumes);
            int currentStart = 1;
            for (int i = 0; i < totalVolumes; i++)
            {
                int count = counts[i];
                int start = currentStart;
                int end = currentStart + count - 1;
                result.Add(new VolumeChapterRange
                {
                    VolumeNumber = i + 1,
                    StartChapter = start,
                    EndChapter = end,
                    TargetChapterCount = count
                });
                currentStart = end + 1;
            }

            AssertInvariants(result, totalChapters);
            return result;
        }

        private static double[] BuildWeights(int totalVolumes)
        {
            if (totalVolumes == FiveActWeights.Length)
                return (double[])FiveActWeights.Clone();

            var weights = new double[totalVolumes];

            if (totalVolumes <= FiveActWeights.Length)
            {
                for (int i = 0; i < totalVolumes; i++)
                    weights[i] = FiveActWeights[i];
            }
            else
            {
                for (int i = 0; i < totalVolumes; i++)
                {
                    double t = i / (double)(totalVolumes - 1) * (FiveActWeights.Length - 1);
                    int lo = (int)Math.Floor(t);
                    int hi = Math.Min(lo + 1, FiveActWeights.Length - 1);
                    double frac = t - lo;
                    weights[i] = FiveActWeights[lo] * (1 - frac) + FiveActWeights[hi] * frac;
                }
            }

            return weights;
        }

        private static int[] DistributeChapters(double[] weights, int totalChapters)
        {
            int n = weights.Length;
            double weightSum = 0;
            for (int i = 0; i < n; i++) weightSum += weights[i];

            var exact = new double[n];
            for (int i = 0; i < n; i++)
                exact[i] = weights[i] / weightSum * totalChapters;

            var counts = new int[n];
            double[] remainders = new double[n];
            int allocated = 0;
            for (int i = 0; i < n; i++)
            {
                counts[i] = Math.Max(1, (int)Math.Floor(exact[i]));
                allocated += counts[i];
                remainders[i] = exact[i] - counts[i];
            }

            int remaining = totalChapters - allocated;
            if (remaining > 0)
            {
                var indices = new int[n];
                for (int i = 0; i < n; i++) indices[i] = i;
                Array.Sort(indices, (a, b) => remainders[b].CompareTo(remainders[a]));
                for (int k = 0; k < remaining && k < n; k++)
                    counts[indices[k]]++;
            }
            else if (remaining < 0)
            {
                var indices = new int[n];
                for (int i = 0; i < n; i++) indices[i] = i;
                Array.Sort(indices, (a, b) => remainders[a].CompareTo(remainders[b]));
                for (int k = 0; k < -remaining && k < n; k++)
                {
                    if (counts[indices[k]] > 1) counts[indices[k]]--;
                }
            }

            return counts;
        }

        private static void AssertInvariants(List<VolumeChapterRange> ranges, int totalChapters)
        {
            if (ranges.Count == 0) return;

            if (ranges[0].StartChapter != 1)
                throw new InvalidOperationException($"[ChapterAllocationHelper] 不变式违反：第1卷StartChapter={ranges[0].StartChapter}，应为1");

            if (ranges[^1].EndChapter != totalChapters)
                throw new InvalidOperationException($"[ChapterAllocationHelper] 不变式违反：末卷EndChapter={ranges[^1].EndChapter}，应为{totalChapters}");

            for (int i = 1; i < ranges.Count; i++)
            {
                if (ranges[i].StartChapter != ranges[i - 1].EndChapter + 1)
                    throw new InvalidOperationException(
                        $"[ChapterAllocationHelper] 不变式违反：第{i + 1}卷StartChapter={ranges[i].StartChapter}，应为{ranges[i - 1].EndChapter + 1}");
            }
        }
    }

    public class VolumeChapterRange
    {
        public int VolumeNumber { get; set; }

        public int StartChapter { get; set; }

        public int EndChapter { get; set; }

        public int TargetChapterCount { get; set; }
    }
}
