using Microsoft.EntityFrameworkCore;
using System.Numerics;

namespace Software_Project_P3_Death_Marble
{
    public static class StageRepository
    {
        public static List<StageData> LoadStages()
        {
            using Data data = new();
            data.Database.EnsureCreated();

            return data.Stages
                .AsNoTracking()
                .OrderBy(stage => stage.Name)
                .ToList();
        }

        internal static void EnsureDefaultStage(Data data)
        {
            MigratePreviousDefaultStage(data);

            if (data.Stages.Any())
                return;

            data.Stages.Add(new StageData());
            data.SaveChanges();
        }

        private static void MigratePreviousDefaultStage(Data data)
        {
            Vector2[] previousDefaultPositions =
            [
                new(0, 0), new(1, 0), new(2, 0), new(2, 1),
                new(2, 2), new(1, 2), new(0, 2), new(0, 1)
            ];

            StageData? previousDefault = data.Stages.FirstOrDefault(stage =>
                stage.Name == "기본 스테이지");
            if (previousDefault == null ||
                previousDefault.Cards.Count != 0 ||
                previousDefault.Map.Tiles.Count != previousDefaultPositions.Length ||
                previousDefault.Map.Tiles.Any(tile => tile.Type != TileType.Zone) ||
                !previousDefaultPositions.ToHashSet().SetEquals(
                    previousDefault.Map.Tiles.Select(tile => tile.Position)))
                return;

            previousDefault.Name = "빈 스테이지";
            previousDefault.Map = MapData.CreateDefault();
            data.SaveChanges();
        }

        public static void SaveStage(StageData stage)
        {
            ArgumentNullException.ThrowIfNull(stage);

            using Data data = new();
            data.Database.EnsureCreated();

            StageData? existing = data.Stages.Find(stage.Id);
            if (existing == null)
            {
                data.Stages.Add(stage);
            }
            else
            {
                existing.Name = stage.Name;
                existing.Map = stage.Map;
                existing.Cards = stage.Cards;
            }

            data.SaveChanges();
        }

        public static void RemoveStage(Guid stageId)
        {
            using Data data = new();
            data.Database.EnsureCreated();

            StageData? stage = data.Stages.Find(stageId);
            if (stage == null)
                return;

            data.Stages.Remove(stage);
            data.SaveChanges();
        }
    }
}
