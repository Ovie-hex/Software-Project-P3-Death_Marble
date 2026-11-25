using System;
namespace Software_Project_P3_Death_Marble;

internal static class DiceProbability
{
    public static double PreferredChance(int hp, int maxHP) => 1.0 / 6 + (0.4 - 1.0 / 6) * Math.Clamp((double)hp / Math.Max(1, maxHP), 0, 1);
    public static int Roll(Random random, int preferred, int hp, int maxHP)
    {
        preferred = Math.Clamp(preferred, 1, 6);
        if (random.NextDouble() < PreferredChance(hp, maxHP)) return preferred;
        int n = random.Next(1, 6); return n >= preferred ? n + 1 : n;
    }
}