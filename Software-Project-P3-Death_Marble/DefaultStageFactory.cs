using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Software_Project_P3_Death_Marble
{
    internal static class DefaultStageFactory
    {
        public static List<StageData> CreateInitialStages()
        {
            return new List<StageData>
            {
                CreateSpiralDeathStairs(),
                CreateDosanHell(),
                CreateThreeHellsCardFight()
            };
        }

        private static StageData CreateSpiralDeathStairs()
        {
            HashSet<(int X, int Y)> cardPositions = new()
            {
                (2, 0), (7, 3), (5, 7), (0, 5),
                (5, 3), (2, 5), (4, 3), (3, 4)
            };
            HashSet<(int X, int Y)> holePositions = new()
            {
                (7, 5), (0, 2), (5, 2)
            };
            HashSet<(int X, int Y)> healPositions = new()
            {
                (3, 0), (4, 7)
            };
            HashSet<(int X, int Y)> teleportPositions = new()
            {
                (7, 2)
            };
            HashSet<(int X, int Y)> prisonPositions = new()
            {
                (0, 4)
            };

            StageMapBuilder builder = new((x, y) =>
            {
                (int X, int Y) position = (x, y);
                TileType type = cardPositions.Contains(position)
                    ? TileType.Card
                    : holePositions.Contains(position)
                        ? TileType.Hole
                        : healPositions.Contains(position)
                            ? TileType.Heal
                            : teleportPositions.Contains(position)
                                ? TileType.Teleport
                                : prisonPositions.Contains(position)
                                    ? TileType.Prison
                                    : position == (1, 3)
                                        ? TileType.Zone
                                        : TileType.Damage;

                int amount = type switch
                {
                    TileType.Damage => 3 + Math.Abs(3 - x) + Math.Abs(3 - y),
                    TileType.Heal => 10,
                    _ => 1
                };

                return new TileData
                {
                    Position = new Vector2(x, y),
                    Type = type,
                    Amount = amount
                };
            });

            builder.AddCycle(CreateRing(0, 0, 7, 7));
            builder.AddCycle(CreateRing(2, 2, 5, 5));
            builder.AddCycle(CreateRing(3, 3, 4, 4));

            // 외곽에서 안쪽으로 말려 들어간 뒤 다시 바깥 경로로 이어지는 나선 연결입니다.
            builder.Connect((0, 3), (1, 3));
            builder.Connect((1, 3), (2, 3));
            builder.Connect((2, 4), (3, 4));
            builder.Connect((4, 4), (5, 4));
            builder.Connect((5, 3), (6, 3));

            return new StageData
            {
                Name = "나선형 죽음의 계단 💀",
                Map = builder.Build((0, 0)),
                Cards = new List<CardEntry>
                {
                    Card("삼도천 통행료", "저승으로 가는 길에도 통행료는 붙습니다. 자신에게 피해 10.", 2, false,
                        Effect(EffectType.Damage, TargetType.Self, 1, 10)),
                    Card("염라의 검은 봉투", "봉투를 열면 무작위 상대에게 치명적인 판결이 내려집니다.", 1, false,
                        Effect(EffectType.Damage, TargetType.Random, 1, 100)),
                    Card("망자의 회복 부적", "저승길에서 숨을 고릅니다. 체력을 25 회복합니다.", 2,
                        Effect(EffectType.Heal, TargetType.Self, 1, 25)),
                    Card("업보의 메아리", "업보가 상대에게 두 번 돌아갑니다.", 2,
                        Effect(EffectType.Damage, TargetType.Other, 2, 8)),
                    Card("혼백 교환권", "상대와 자리를 바꾸어 운명을 뒤집습니다.", 2,
                        Effect(EffectType.Swap, TargetType.Other, 1, 1)),
                    Card("길 잃은 영혼", "갈 곳을 잊은 영혼이 무작위로 이동합니다.", 1,
                        Effect(EffectType.Teleport, TargetType.Random, 1, 0)),
                    Card("염라의 잔심부름", "상대에게 작은 벌을 세 번 내립니다.", 2,
                        Effect(EffectType.Damage, TargetType.Other, 3, 5)),
                    Card("계단 미끄럼", "발을 헛디뎌 자신이 피해 7을 받습니다.", 2, false,
                        Effect(EffectType.Damage, TargetType.Self, 1, 7)),
                    Card("저승 행정 공문", "서류가 누락되었습니다. 염라대왕도 지금은 아무것도 할 수 없습니다.", 3, false,
                        Effect(EffectType.None, TargetType.Self, 1, 0)),
                    Card("구천의 생명수", "마지막 계단에서 생명수를 얻어 체력을 40 회복합니다.", 1,
                        Effect(EffectType.Heal, TargetType.Self, 1, 40))
                }
            };
        }

        private static StageData CreateDosanHell()
        {
            HashSet<(int X, int Y)> cardPositions = new()
            {
                (4, 2), (1, 1), (7, 4), (5, 3)
            };
            HashSet<(int X, int Y)> holePositions = new()
            {
                (4, 5), (1, 2)
            };

            StageMapBuilder builder = new((x, y) =>
            {
                (int X, int Y) position = (x, y);
                TileType type = cardPositions.Contains(position)
                    ? TileType.Card
                    : holePositions.Contains(position)
                        ? TileType.Hole
                        : TileType.Damage;

                return new TileData
                {
                    Position = new Vector2(x, y),
                    Type = type,
                    Amount = 1
                };
            });

            List<(int X, int Y)> upperLobe =
            [
                (4, 3), (4, 2), (4, 1), (4, 0), (3, 0), (2, 0),
                (1, 0), (1, 1), (1, 2), (1, 3), (2, 3), (3, 3)
            ];
            List<(int X, int Y)> lowerLobe =
            [
                (4, 3), (4, 4), (4, 5), (4, 6), (5, 6), (6, 6),
                (7, 6), (7, 5), (7, 4), (7, 3), (6, 3), (5, 3)
            ];

            builder.AddCycle(upperLobe);
            builder.AddCycle(lowerLobe);

            return new StageData
            {
                Name = "도산지옥",
                Map = builder.Build((4, 3)),
                Cards = new List<CardEntry>
                {
                    Card("고금리 독촉", "이자만큼 체력이 빠져나갑니다. 상대에게 피해 5.", 4,
                        Effect(EffectType.Damage, TargetType.Other, 1, 5)),
                    Card("압류 통지서", "통지서는 예고일 뿐입니다. 그래도 상대는 피해 10을 받습니다.", 3,
                        Effect(EffectType.Damage, TargetType.Other, 1, 10)),
                    Card("연체 이자 3회분", "연체 이자가 세 번 청구됩니다.", 2,
                        Effect(EffectType.Damage, TargetType.Other, 3, 4)),
                    Card("신용 점수 폭락", "무작위 상대의 신용과 체력을 함께 깎습니다.", 2,
                        Effect(EffectType.Damage, TargetType.Random, 1, 15)),
                    Card("담보 회수", "상대가 맡긴 체력 20을 회수합니다.", 2,
                        Effect(EffectType.Damage, TargetType.Other, 1, 20)),
                    Card("연대 보증", "보증을 선 상대가 대신 피해 8을 받습니다.", 2,
                        Effect(EffectType.Damage, TargetType.Other, 1, 8)),
                    Card("전원 채무 추심", "모두에게 소액의 채무를 청구합니다.", 1,
                        Effect(EffectType.Damage, TargetType.All, 1, 5)),
                    Card("독촉 전화", "전화를 다섯 번이나 받습니다. 상대에게 매번 피해 2.", 2,
                        Effect(EffectType.Damage, TargetType.Other, 5, 2)),
                    Card("급여 압류", "월급이 들어오자마자 상대의 체력 25를 압류합니다.", 1,
                        Effect(EffectType.Damage, TargetType.Other, 1, 25)),
                    Card("추심원의 출석", "무작위 상대에게 추심원이 찾아갑니다.", 1,
                        Effect(EffectType.Damage, TargetType.Random, 1, 12)),
                    Card("도산 심사", "서류가 통과되지 않아 상대가 피해를 받습니다.", 1,
                        Effect(EffectType.Damage, TargetType.Other, 1, 12)),
                    Card("회계 장부 오류", "장부가 맞지 않아 아무도 얼마를 빚졌는지 모릅니다. 이번엔 넘어갑니다.", 2, false,
                        Effect(EffectType.None, TargetType.Self, 1, 0)),
                    Card("텅 빈 통장", "통장을 열어 보니 바람 소리만 납니다. 효과도 바람처럼 사라집니다.", 2, false,
                        Effect(EffectType.None, TargetType.Self, 1, 0)),
                    Card("도산지옥 민원서", "민원 접수처가 도산하여 아무 효과도 처리되지 않았습니다.", 1, false,
                        Effect(EffectType.None, TargetType.Self, 1, 0)),
                    Card("면책 결정", "모든 빚을 탕감받고 체력을 최대로 회복합니다.", 1,
                        Effect(EffectType.Heal, TargetType.Self, 1, 100))
                }
            };
        }

        private static StageData CreateThreeHellsCardFight()
        {
            HashSet<(int X, int Y)> deathPositions = new()
            {
                (5, 2), (0, 4)
            };

            StageMapBuilder builder = new((x, y) =>
            {
                bool isDeathTile = deathPositions.Contains((x, y));
                return new TileData
                {
                    Position = new Vector2(x, y),
                    Type = isDeathTile ? TileType.Damage : TileType.Card,
                    Amount = isDeathTile ? 100 : 1
                };
            });

            builder.AddCycle(CreateRing(0, 0, 5, 5));

            return new StageData
            {
                Name = "카드 싸움 안에 단 3개의 지옥",
                Map = builder.Build((0, 0)),
                Cards = new List<CardEntry>
                {
                    Card("운명의 자리 바꾸기", "상대와 위치를 바꾸어 전세를 뒤집습니다.", 3,
                        Effect(EffectType.Swap, TargetType.Other, 1, 1)),
                    Card("무작위 교환", "누구와 자리가 바뀔지는 운에 맡깁니다.", 4,
                        Effect(EffectType.Swap, TargetType.Random, 1, 1)),
                    Card("전원 재배치", "모든 플레이어의 위치를 뒤섞습니다.", 2,
                        Effect(EffectType.Swap, TargetType.All, 1, 1)),
                    Card("역전의 수", "상대와 자리를 두 번 교환합니다.", 2,
                        Effect(EffectType.Swap, TargetType.Other, 2, 1)),
                    Card("혼돈의 스왑", "무작위 상대와 자리를 교환합니다.", 3,
                        Effect(EffectType.Swap, TargetType.Random, 1, 1)),
                    Card("자폭 스위치", "사용자 본인에게 피해 100을 주는 위험한 카드입니다.", 1,
                        Effect(EffectType.Damage, TargetType.Self, 1, 100))
                }
            };
        }

        private static List<(int X, int Y)> CreateRing(
            int minX,
            int minY,
            int maxX,
            int maxY)
        {
            List<(int X, int Y)> positions = new();

            for (int x = minX; x <= maxX; x++)
                positions.Add((x, minY));

            for (int y = minY + 1; y <= maxY; y++)
                positions.Add((maxX, y));

            for (int x = maxX - 1; x >= minX; x--)
                positions.Add((x, maxY));

            for (int y = maxY - 1; y > minY; y--)
                positions.Add((minX, y));

            return positions;
        }

        private static CardEntry Card(
            string name,
            string description,
            int copies,
            params EffectData[] effects)
        {
            return Card(name, description, copies, true, effects);
        }

        private static CardEntry Card(
            string name,
            string description,
            int copies,
            bool usable,
            params EffectData[] effects)
        {
            return new CardEntry
            {
                Count = copies,
                Card = new CardData
                {
                    Name = name,
                    Description = description,
                    Usable = usable,
                    Effects = effects.ToList()
                }
            };
        }

        private static EffectData Effect(
            EffectType type,
            TargetType target,
            int count,
            int amount)
        {
            return new EffectData
            {
                Type = type,
                Target = target,
                Count = count,
                Amount = amount
            };
        }

        private sealed class StageMapBuilder
        {
            private readonly Func<int, int, TileData> createTile;
            private readonly Dictionary<(int X, int Y), TileData> tiles = new();

            public StageMapBuilder(Func<int, int, TileData> createTile)
            {
                this.createTile = createTile;
            }

            public void AddCycle(IReadOnlyList<(int X, int Y)> positions)
            {
                if (positions.Count < 3)
                    throw new ArgumentException("순환 경로에는 최소 세 타일이 필요합니다.");

                foreach ((int x, int y) in positions)
                    GetTile(x, y);

                for (int i = 0; i < positions.Count; i++)
                    Connect(positions[i], positions[(i + 1) % positions.Count]);
            }

            public void Connect(
                (int X, int Y) fromPosition,
                (int X, int Y) toPosition)
            {
                if (Math.Abs(fromPosition.X - toPosition.X) +
                    Math.Abs(fromPosition.Y - toPosition.Y) != 1)
                {
                    throw new InvalidOperationException(
                        $"기본 스테이지 경로가 인접하지 않습니다: {fromPosition} → {toPosition}");
                }

                TileData from = GetTile(fromPosition.X, fromPosition.Y);
                TileData to = GetTile(toPosition.X, toPosition.Y);

                if (!from.Next.Contains(to))
                    from.Next.Add(to);

                if (!to.Prev.Contains(from))
                    to.Prev.Add(from);
            }

            public MapData Build((int X, int Y) startPosition)
            {
                MapData map = new()
                {
                    Start = GetTile(startPosition.X, startPosition.Y),
                    Tiles = tiles.Values.ToList()
                };
                map.IsLinked = map.HasConnectedPaths();
                return map;
            }

            private TileData GetTile(int x, int y)
            {
                if (tiles.TryGetValue((x, y), out TileData? tile))
                    return tile;

                tile = createTile(x, y);
                tiles[(x, y)] = tile;
                return tile;
            }
        }
    }
}
