namespace Software_Project_P3_Death_Marble
{
    internal static class StageConnectorNames
    {
        public const string LoadStages = "loadStageEv";
        public const string SaveStage = "saveStageEv";
        public const string RemoveStage = "removeStageEv";

        public const string DataManager = "DataManager";
        public const string SettingPage = "SettingPage";
        public const string StageCustomPage = "StageCustomPage";
    }

    internal sealed class LoadStagesRequest { }

    internal sealed class SaveStageRequest
    {
        public StageData Stage { get; set; } = new();
    }

    internal sealed class RemoveStageRequest
    {
        public Guid StageId { get; set; }
    }

    internal sealed class StageEventResult
    {
        public bool Success { get; set; }
        public List<StageData>? Stages { get; set; }
        public string? ErrorMessage { get; set; }

        public static StageEventResult Succeeded(List<StageData>? stages = null)
        {
            return new StageEventResult
            {
                Success = true,
                Stages = stages
            };
        }

        public static StageEventResult Failed(Exception exception)
        {
            return new StageEventResult
            {
                Success = false,
                ErrorMessage = exception.Message
            };
        }
    }
}
