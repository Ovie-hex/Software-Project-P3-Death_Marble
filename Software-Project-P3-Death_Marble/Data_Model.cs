using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Software_Project_P3_Death_Marble
{
    // ------------------
    // Enum
    // ------------------
    public enum TileType
    {
        Zone = 0,
        Damage = 1,
        Heal = 2,
        Card = 3,
        Prison = 4,
        Teleport = 5,
        Hole = 6,
    }

    public enum EffectType
    {
        None = 0,
        Damage = 1,
        Heal = 2,
        Teleport = 4,
        Swap = 5,
    }

    public enum TargetType
    {
        Self = 0,
        Other = 1,
        All = 2,
        Random = 3, // [수정] 오타 수정 (Ramdom -> Random)
    }

    // ------------------
    // Model (순환 참조 및 가변성을 고려하여 class 사용)
    // ------------------
    public class StageData
    {
        [Key]
        public Guid Id { get; init; } = Guid.NewGuid();
        public string Name { get; set; } = "빈 스테이지";

        // [수정] 기본 맵 생성 팩토리 메서드 호출
        public MapData Map { get; set; } = MapData.CreateDefault();

        // [수정] 객체 Key 딕셔너리 대신 직렬화 및 조회가 안전한 List<CardEntry> 구조 권장
        public List<CardEntry> Cards { get; set; } = new();
    }

    // [추가] Dictionary<CardData, int>를 대체할 직렬화 친화적 래퍼 클래스
    public class CardEntry
    {
        public CardData Card { get; set; } = new();
        public int Count { get; set; } = 1;
    }

    public class MapData
    {
        public TileData? Start { get; set; }
        public bool IsLinked { get; set; }
        public List<TileData> Tiles { get; set; } = new();

        public void RemoveNonAdjacentConnections()
        {
            foreach (TileData tile in Tiles)
            {
                tile.Next.RemoveAll(next =>
                    Math.Abs(tile.Position.X - next.Position.X) +
                    Math.Abs(tile.Position.Y - next.Position.Y) != 1);
            }

            foreach (TileData tile in Tiles)
                tile.Prev.RemoveAll(previous => !previous.Next.Contains(tile));
        }

        public bool HasConnectedPaths()
        {
            if (Start == null || Tiles.Count == 0)
                return false;

            TileData? startTile = Tiles.FirstOrDefault(tile =>
                ReferenceEquals(tile, Start))
                ?? Tiles.FirstOrDefault(tile => tile.Position == Start.Position);
            if (startTile == null)
                return false;

            TileData? ResolveMapTile(TileData tile)
            {
                return Tiles.FirstOrDefault(candidate =>
                    ReferenceEquals(candidate, tile))
                    ?? Tiles.FirstOrDefault(candidate =>
                        candidate.Position == tile.Position);
            }

            HashSet<TileData> visited = new() { startTile };
            Queue<TileData> pending = new();
            foreach (TileData next in startTile.Next)
            {
                TileData? mapTile = ResolveMapTile(next);
                if (mapTile == null)
                    continue;

                if (ReferenceEquals(mapTile, startTile))
                    return true;

                if (visited.Add(mapTile))
                    pending.Enqueue(mapTile);
            }

            while (pending.Count > 0)
            {
                TileData current = pending.Dequeue();
                foreach (TileData next in current.Next)
                {
                    TileData? mapTile = ResolveMapTile(next);
                    if (mapTile == null)
                        continue;

                    if (ReferenceEquals(mapTile, startTile))
                        return true;

                    if (visited.Add(mapTile))
                        pending.Enqueue(mapTile);
                }
            }

            return false;
        }

        // [수정] JSON 역직렬화 시 타일이 중복 생성되지 않도록 기본 생성자는 비워둠
        public MapData() { }

        // 기본 9x9 도넛형 순환 맵 생성
        public static MapData CreateDefault()
        {
            var map = new MapData();
            List<Vector2> positions = new(32);

            for (int x = 0; x < 9; x++)
                positions.Add(new Vector2(x, 0));

            for (int y = 1; y < 9; y++)
                positions.Add(new Vector2(8, y));

            for (int x = 7; x >= 0; x--)
                positions.Add(new Vector2(x, 8));

            for (int y = 7; y > 0; y--)
                positions.Add(new Vector2(0, y));

            for (int i = 0; i < positions.Count; i++)
            {
                map.Tiles.Add(new TileData { Position = positions[i] });
            }

            for (int i = 0; i < map.Tiles.Count; i++)
            {
                int prevIndex = (i - 1 + map.Tiles.Count) % map.Tiles.Count;
                int nextIndex = (i + 1) % map.Tiles.Count;
                map.Tiles[i].Prev.Add(map.Tiles[prevIndex]);
                map.Tiles[i].Next.Add(map.Tiles[nextIndex]);
            }

            map.Start = map.Tiles[0];
            map.IsLinked = true;
            return map;
        }
    }

    // [수정] Prev/Next 순환 참조로 인한 StackOverflowException 방지를 위해 class로 변경
    public class TileData
    {
        public Vector2 Position { get; set; }
        public TileType Type { get; set; } = TileType.Zone;
        public int Amount { get; set; } = 1;
        public List<TileData> Prev { get; set; } = new();
        public List<TileData> Next { get; set; } = new();
    }

    public class CardData
    {
        public string Name { get; set; } = "빈 카드";
        public string Description { get; set; } = "해당 카드의 설명을 입력하세요.";
        public bool Usable { get; set; } = true;
        public List<EffectData> Effects { get; set; } = [new()];
    }

    public class EffectData
    {
        public EffectType Type { get; set; } = EffectType.None;
        public TargetType Target { get; set; } = TargetType.Self;
        public int Count { get; set; } = 1;
        public int Amount { get; set; } = 1;
    }

    // ------------------
    // Database Context (SQLite)
    // ------------------
    public class Data : DbContext
    {
        public DbSet<StageData> Stages { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            string userFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string dbFolder = Path.Combine(userFolder, "Death_Marble.Datas");
            Directory.CreateDirectory(dbFolder);
            string dbPath = Path.Combine(dbFolder, "Stage.db");

            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var jsonOptions = new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.Preserve,
                IncludeFields = true
            };

            // 1. MapData 변환기 + 깊은 복사 ValueComparer 적용
            modelBuilder.Entity<StageData>()
                .Property(s => s.Map)
                .HasConversion(
                    v => SerializeMap(v),
                    v => DeserializeMap(v)
                )
                .Metadata.SetValueComparer(CreateMapValueComparer());

            // 2. List<CardEntry> 변환기 + 깊은 복사 ValueComparer 적용
            modelBuilder.Entity<StageData>()
                .Property(s => s.Cards)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, jsonOptions),
                    v => JsonSerializer.Deserialize<List<CardEntry>>(v, jsonOptions) ?? new()
                )
                .Metadata.SetValueComparer(CreateJsonValueComparer<List<CardEntry>>(jsonOptions));
        }

        private static readonly JsonSerializerOptions MapSnapshotOptions = new()
        {
            IncludeFields = true
        };

        private static readonly JsonSerializerOptions LegacyMapOptions = new()
        {
            ReferenceHandler = ReferenceHandler.Preserve,
            IncludeFields = true
        };

        private static string SerializeMap(MapData? map)
        {
            if (map == null)
                return "null";

            List<TileData> tiles = map.Tiles.ToList();
            int startIndex = FindTileIndex(tiles, map.Start);
            if (map.Start != null && startIndex < 0)
            {
                tiles.Insert(0, map.Start);
                startIndex = 0;
            }

            int IndexOf(TileData tile) => FindTileIndex(tiles, tile);

            MapSnapshot snapshot = new()
            {
                IsLinked = map.IsLinked,
                StartIndex = startIndex >= 0 ? startIndex : null,
                Tiles = tiles.Select(tile => new TileSnapshot
                {
                    Position = tile.Position,
                    Type = tile.Type,
                    Amount = tile.Amount,
                    PrevIndices = tile.Prev
                        .Select(IndexOf)
                        .Where(index => index >= 0)
                        .ToList(),
                    NextIndices = tile.Next
                        .Select(IndexOf)
                        .Where(index => index >= 0)
                        .ToList()
                }).ToList()
            };

            return JsonSerializer.Serialize(snapshot, MapSnapshotOptions);
        }

        private static MapData DeserializeMap(string json)
        {
            if (json == "null")
                return new MapData();

            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("Version", out _))
            {
                MapSnapshot? snapshot = JsonSerializer.Deserialize<MapSnapshot>(
                    json,
                    MapSnapshotOptions);
                if (snapshot == null)
                    return new MapData();

                MapData map = new()
                {
                    IsLinked = snapshot.IsLinked,
                    Tiles = snapshot.Tiles.Select(tile => new TileData
                    {
                        Position = tile.Position,
                        Type = tile.Type,
                        Amount = tile.Amount
                    }).ToList()
                };

                for (int i = 0; i < map.Tiles.Count; i++)
                {
                    TileSnapshot tile = snapshot.Tiles[i];
                    map.Tiles[i].Prev.AddRange(ResolveTiles(map.Tiles, tile.PrevIndices));
                    map.Tiles[i].Next.AddRange(ResolveTiles(map.Tiles, tile.NextIndices));
                }

                if (snapshot.StartIndex is int startIndex &&
                    startIndex >= 0 && startIndex < map.Tiles.Count)
                    map.Start = map.Tiles[startIndex];

                return map;
            }

            // 기존 ReferenceHandler.Preserve JSON 형식과의 호환
            return JsonSerializer.Deserialize<MapData>(json, LegacyMapOptions)
                ?? new MapData();
        }

        private static List<TileData> ResolveTiles(
            List<TileData> tiles,
            IEnumerable<int> indices)
        {
            return indices
                .Where(index => index >= 0 && index < tiles.Count)
                .Select(index => tiles[index])
                .ToList();
        }

        private static int FindTileIndex(List<TileData> tiles, TileData? target)
        {
            if (target == null)
                return -1;

            int index = tiles.FindIndex(tile => ReferenceEquals(tile, target));
            return index >= 0
                ? index
                : tiles.FindIndex(tile => tile.Position == target.Position);
        }

        private static ValueComparer<MapData> CreateMapValueComparer()
        {
            return new ValueComparer<MapData>(
                (left, right) => SerializeMap(left) == SerializeMap(right),
                map => SerializeMap(map).GetHashCode(),
                map => map == null ? null! : DeserializeMap(SerializeMap(map))
            );
        }

        private sealed class MapSnapshot
        {
            public MapSnapshot() { }

            public int Version { get; set; } = 1;
            public bool IsLinked { get; set; }
            public int? StartIndex { get; set; }
            public List<TileSnapshot> Tiles { get; set; } = new();
        }

        private sealed class TileSnapshot
        {
            public TileSnapshot() { }

            public Vector2 Position { get; set; }
            public TileType Type { get; set; }
            public int Amount { get; set; }
            public List<int> PrevIndices { get; set; } = new();
            public List<int> NextIndices { get; set; } = new();
        }

        // [추가] 내부 프로퍼티 수정까지 완벽히 감지하는 범용 JSON ValueComparer
        private static ValueComparer<T> CreateJsonValueComparer<T>(JsonSerializerOptions options) where T : class, new()
        {
            return new ValueComparer<T>(
                (c1, c2) => JsonSerializer.Serialize(c1, options) == JsonSerializer.Serialize(c2, options),
                c => JsonSerializer.Serialize(c, options).GetHashCode(),
                c => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(c, options), options) ?? new T()
            );
        }
    }
}
