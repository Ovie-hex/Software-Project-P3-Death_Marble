using System;
using System.Collections.Generic;
namespace Software_Project_P3_Death_Marble;
internal static class PathAdvisor
{
    public static int Score(int tile, int steps, Dictionary<int,List<int>> paths, Func<int,int> score)
    {
        var memo = new Dictionary<(int,int),int>();
        int Search(int t,int left)
        {
            if(left<=0 || !paths.TryGetValue(t,out var next) || next.Count==0) return score(t);
            if(memo.TryGetValue((t,left),out int cached)) return cached;
            int best=int.MinValue;
            foreach(int n in next) best=Math.Max(best,Search(n,left-1));
            return memo[(t,left)]=best;
        }
        return Search(tile,steps-1);
    }
    public static int Recommend(List<int> choices,int steps,Dictionary<int,List<int>> paths,Func<int,int> score)
    {
        int result=choices[0], best=int.MinValue;
        foreach(int tile in choices) { int value=Score(tile,steps,paths,score); if(value>best){best=value;result=tile;} }
        return result;
    }
}
