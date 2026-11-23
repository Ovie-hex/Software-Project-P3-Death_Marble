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

        // [수정] JSON 역직렬화 시 타일이 중복 생성되지 않도록 기본 생성자는 비워둠
        public MapData() { }

        // [추가] 기본 8칸 맵 생성용 정적 메서드
        public static MapData CreateDefault()
        {
            var map = new MapData();
            Vector2[] positions = [
                new(0, 0), new(1, 0), new(2, 0),
                new(2, 1), new(2, 2), new(1, 2),
                new(0, 2), new(0, 1)
            ];

            for (int i = 0; i < positions.Length; i++)
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
                    v => JsonSerializer.Serialize(v, jsonOptions),
                    v => JsonSerializer.Deserialize<MapData>(v, jsonOptions) ?? new MapData()
                )
                .Metadata.SetValueComparer(CreateJsonValueComparer<MapData>(jsonOptions));

            // 2. List<CardEntry> 변환기 + 깊은 복사 ValueComparer 적용
            modelBuilder.Entity<StageData>()
                .Property(s => s.Cards)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, jsonOptions),
                    v => JsonSerializer.Deserialize<List<CardEntry>>(v, jsonOptions) ?? new()
                )
                .Metadata.SetValueComparer(CreateJsonValueComparer<List<CardEntry>>(jsonOptions));
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
