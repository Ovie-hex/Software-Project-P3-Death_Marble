using System;
using System.Collections.Generic;

namespace Software_Project_P3_Death_Marble
{
    internal static class GameConnectorNames
    {
        public const string Bus = "deathMarbleGameBus";
        public const string GameManager = "GameManager";
        public const string MainGame = "MainGame";
    }

    internal enum GameCommandType
    {
        Initialize,
        RollDice,
        AdvanceMovement,
        SelectPath,
        SelectTeleportDestination,
        UseCard,
        SelectCardTarget,
        CancelCardTarget
    }

    internal sealed class GameCommand
    {
        public GameCommandType Type { get; set; }
        public StageData? Stage { get; set; }
        public List<int> TurnOrder { get; set; } = new();
        public int? PreferredDiceNumber { get; set; }
        public int Index { get; set; } = -1;
        public int TileIndex { get; set; } = -1;
        public int PlayerId { get; set; } = -1;
    }

    internal enum GameMessageType
    {
        StateChanged,
        Log,
        DiceRolled,
        PlayerMoved,
        MovementFinished,
        PathSelectionRequired,
        TeleportSelectionRequired,
        CardTargetSelectionRequired,
        GameEnded,
        Error
    }

    internal sealed class GameManagerMessage
    {
        public GameMessageType Type { get; set; }
        public GameStateSnapshot? State { get; set; }
        public string Text { get; set; } = string.Empty;
        public int PlayerId { get; set; } = -1;
        public int Value { get; set; }
        public int? WinnerId { get; set; }
        public int CardIndex { get; set; } = -1;
        public bool IsImmediateCard { get; set; }
        public List<int> OptionIndices { get; set; } = new();
    }

    internal sealed class GameStateSnapshot
    {
        public List<GamePlayerSnapshot> Players { get; set; } = new();
        public List<int> TurnOrder { get; set; } = new();
        public int CurrentPlayerId { get; set; } = -1;
        public int CurrentRound { get; set; } = 1;
        public int MaximumRound { get; set; }
        public double PreferredDiceChance { get; set; }
        public bool IsSuddenDeath { get; set; }
        public int RemainingMoves { get; set; }
        public bool IsGameRunning { get; set; }
        public bool IsMoving { get; set; }
        public bool HasRolledThisTurn { get; set; }
        public bool IsWaitingForPath { get; set; }
        public bool IsWaitingForTeleport { get; set; }
        public bool IsWaitingForCardTarget { get; set; }
    }

    internal sealed class GamePlayerSnapshot
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int HP { get; set; }
        public int MaxHP { get; set; }
        public int TileIndex { get; set; } = -1;
        public int PrisonTurns { get; set; }
        public bool IsEliminated { get; set; }
        public List<CardData> Cards { get; set; } = new();
    }

}
