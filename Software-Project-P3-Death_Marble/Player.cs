using System;
using System.Collections.Generic;

namespace Software_Project_P3_Death_Marble
{
    public class Player
    {
        // 플레이어 고유 번호
        public int Id { get; set; }

        // 플레이어 이름
        public string Name { get; set; }

        // 현재 HP
        public int HP { get; private set; }

        // 최대 HP
        public int MaxHP { get; set; } = 100;

        // 현재 위치
        public TileData? CurrentTile { get; set; }

        // 봇 여부
        public bool IsBot { get; set; }

        // 탈락 여부
        public bool IsEliminated => HP <= 0;

        // 보유 카드
        public List<CardData> Cards { get; set; }

        // 선호 주사위 숫자
        public int PreferredDiceNumber { get; set; } = 1;

        public Player(
            int id,
            string name,
            TileData startTile,
            bool isBot = false)
        {
            Id = id;
            Name = name;
            HP = 100;
            CurrentTile = startTile;
            IsBot = isBot;

            Cards = new List<CardData>();
        }

        // HP 감소
        public void TakeDamage(int amount)
        {
            if (amount <= 0)
                return;

            HP = Math.Max(0, HP - amount);
        }

        // HP 회복
        public void Heal(int amount)
        {
            if (amount <= 0)
                return;

            HP = Math.Min(MaxHP, HP + amount);
        }

        // 카드 획득
        public bool AddCard(CardData card)
        {
            if (Cards.Count >= 3)
                return false;

            Cards.Add(card);
            return true;
        }

        // 카드 제거
        public bool RemoveCard(CardData card)
        {
            return Cards.Remove(card);
        }

        // 플레이어 초기화
        public void Reset(TileData startTile)
        {
            HP = MaxHP;
            CurrentTile = startTile;
            Cards.Clear();
        }
    }
}