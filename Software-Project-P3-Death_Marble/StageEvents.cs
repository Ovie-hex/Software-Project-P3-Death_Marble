namespace Software_Project_P3_Death_Marble
{
    internal static class StageConnectorNames
    {
        public const string LoadStages = "loadStageEv";
        public const string SaveStage = "saveStageEv";
        public const string RemoveStage = "removeStageEv";

        public const string DataManager = "DataManager";
        public const string StageCustomizer = "StageCustomizer";
        public const string SettingPage = "SettingPage";
        public const string StageCustomPage = "StageCustomPage";
        public const string StageCustomizerCommands = "stageCustomizerCommandsEv";
    }

    internal sealed class LoadStagesRequest
    {
        public Guid RequestId { get; set; }
    }

    internal sealed class SaveStageRequest
    {
        public Guid RequestId { get; set; }
        public StageData Stage { get; set; } = new();
    }

    internal sealed class RemoveStageRequest
    {
        public Guid StageId { get; set; }
    }

    internal sealed class StageEventResult
    {
        public Guid RequestId { get; set; }
        public bool Success { get; set; }
        public List<StageData>? Stages { get; set; }
        public string? ErrorMessage { get; set; }

        public static StageEventResult Succeeded(
            List<StageData>? stages = null,
            Guid requestId = default)
        {
            return new StageEventResult
            {
                RequestId = requestId,
                Success = true,
                Stages = stages
            };
        }

        public static StageEventResult Failed(
            Exception exception,
            Guid requestId = default)
        {
            return new StageEventResult
            {
                RequestId = requestId,
                Success = false,
                ErrorMessage = exception.Message
            };
        }
    }

    internal sealed class LoadStageEditorRequest
    {
        public Guid RequestId { get; set; }
        public Guid SessionId { get; set; }
        public Guid? StageId { get; set; }
    }

    internal sealed class StageEditorPosition
    {
        public int X { get; set; }
        public int Y { get; set; }
    }

    internal sealed class StageEditorTileDraft
    {
        public int X { get; set; }
        public int Y { get; set; }
        public TileType Type { get; set; }
        public int Amount { get; set; } = 1;
        public List<StageEditorPosition> Next { get; set; } = new();
    }

    internal sealed class StageEditorDraft
    {
        public Guid? StageId { get; set; }
        public string Name { get; set; } = string.Empty;
        public StageEditorPosition? Start { get; set; }
        public List<StageEditorTileDraft> Tiles { get; set; } = new();
        public List<CardEntry> Cards { get; set; } = new();
    }

    internal sealed class SaveStageEditorRequest
    {
        public Guid RequestId { get; set; }
        public Guid SessionId { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    internal sealed class CloseStageEditorRequest
    {
        public Guid SessionId { get; set; }
    }

    internal enum StageEditorMapAction
    {
        AddTile,
        UpdateTile,
        ConnectTiles,
        RemoveTile,
        ClearMap
    }

    internal sealed class StageEditorMapActionRequest
    {
        public Guid RequestId { get; set; }
        public Guid SessionId { get; set; }
        public StageEditorMapAction Action { get; set; }
        public StageEditorPosition? First { get; set; }
        public StageEditorPosition? Second { get; set; }
        public TileType TileType { get; set; }
        public int Amount { get; set; } = 1;
    }

    internal enum StageEditorCardAction
    {
        AddCard,
        DeleteCard,
        SetName,
        SetDescription,
        SetCount,
        SetUsable,
        AddEffect,
        DeleteEffect,
        SetEffectType,
        SetEffectTarget,
        SetEffectCount,
        SetEffectAmount
    }

    internal sealed class StageEditorCardActionRequest
    {
        public Guid RequestId { get; set; }
        public Guid SessionId { get; set; }
        public StageEditorCardAction Action { get; set; }
        public int CardIndex { get; set; } = -1;
        public int EffectIndex { get; set; } = -1;
        public string Text { get; set; } = string.Empty;
        public int Value { get; set; }
        public bool IsChecked { get; set; }
        public EffectType EffectType { get; set; }
        public TargetType TargetType { get; set; }
    }

    internal sealed class StageEditorCardActionResult
    {
        public Guid RequestId { get; set; }
        public StageEditorCardAction Action { get; set; }
        public bool Success { get; set; }
        public StageEditorDraft? Draft { get; set; }
        public int SelectedCardIndex { get; set; } = -1;
        public int SelectedEffectIndex { get; set; } = -1;
        public string? ErrorMessage { get; set; }
    }

    internal sealed class StageEditorMapActionResult
    {
        public Guid RequestId { get; set; }
        public bool Success { get; set; }
        public StageEditorDraft? Draft { get; set; }
        public StageEditorPosition? SelectedTile { get; set; }
        public string? Message { get; set; }
        public int CoordinateShiftX { get; set; }
        public int CoordinateShiftY { get; set; }
    }

    internal sealed class StageEditorLoadResult
    {
        public Guid RequestId { get; set; }
        public Guid SessionId { get; set; }
        public bool Success { get; set; }
        public StageEditorDraft? Draft { get; set; }
        public string? ErrorMessage { get; set; }
    }

    internal sealed class StageEditorSaveResult
    {
        public Guid RequestId { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public int? InvalidCardIndex { get; set; }
    }
}
