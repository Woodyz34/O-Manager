using System;
using System.Collections.Generic;

namespace MapImport
{
    // The player's answers about contours the game was unsure of (2026-11-27, Anders: "Ska den vara högre eller lägre
    // än den här höjdkurvan?"). Asked in Edit map (HeightQuestionPanel); kept in map.json (MapPackageInfo.heightAnswers)
    // and in points.json, where the 3D ground reads them (RunContourField.ReadAnswers). Five numbers each, in game
    // metres: a point on contour A, a point on contour B next to it, and 1 when A is higher, -1 lower, 0 as high.
    public static class HeightAnswers
    {
        public const int Size = 5;
        public const int Max = 3000;   // answers kept for one map

        // Only whole answers with real numbers, at most Max of them (another player's shared map is read with this
        // too); null when there are none
        public static double[] Clean(double[] answers)
        {
            if (answers == null || answers.Length < Size) return null;
            var kept = new List<double>();
            for (int i = 0; i + Size <= answers.Length && kept.Count < Max * Size; i += Size)
            {
                bool ok = true;
                for (int k = 0; k < Size; k++) ok &= !double.IsNaN(answers[i + k]) && !double.IsInfinity(answers[i + k]) && Math.Abs(answers[i + k]) < 1e8;
                if (!ok) continue;
                for (int k = 0; k < 4; k++) kept.Add(answers[i + k]);
                kept.Add(Math.Sign(answers[i + 4]));
            }
            return kept.Count > 0 ? kept.ToArray() : null;
        }

        // The answers with one more: an earlier answer about the same place (both points within 3 m) is replaced
        public static double[] With(double[] answers, double[] answer)
        {
            var list = new List<double>();
            double[] old = Clean(answers);
            if (old != null)
                for (int i = 0; i + Size <= old.Length; i += Size)
                {
                    bool same = Near(old[i], old[i + 1], answer[0], answer[1]) && Near(old[i + 2], old[i + 3], answer[2], answer[3])
                                || Near(old[i], old[i + 1], answer[2], answer[3]) && Near(old[i + 2], old[i + 3], answer[0], answer[1]);
                    if (!same) for (int k = 0; k < Size; k++) list.Add(old[i + k]);
                }
            list.AddRange(answer);
            return Clean(list.ToArray());
        }

        // The answers as RunContourField.Build takes them
        public static List<double[]> AsList(double[] answers)
        {
            var list = new List<double[]>();
            double[] clean = Clean(answers);
            if (clean == null) return list;
            for (int i = 0; i + Size <= clean.Length; i += Size)
            {
                var a = new double[Size];
                Array.Copy(clean, i, a, 0, Size);
                list.Add(a);
            }
            return list;
        }

        public static int Count(double[] answers) => answers == null ? 0 : answers.Length / Size;

        static bool Near(double x0, double y0, double x1, double y1) => Math.Abs(x0 - x1) < 3 && Math.Abs(y0 - y1) < 3;
    }
}
