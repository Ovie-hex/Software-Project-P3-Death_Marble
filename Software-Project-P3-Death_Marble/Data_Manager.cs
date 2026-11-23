namespace Software_Project_P3_Death_Marble
{
    internal sealed class Data_Manager
    {
        private bool defaultStageInitialized;

        private readonly Connector.BindableEvent loadStagesEvent;
        private readonly Connector.BindableEvent saveStageEvent;
        private readonly Connector.BindableEvent removeStageEvent;

        public Data_Manager()
        {
            loadStagesEvent = Connector.GetBindableEvent(StageConnectorNames.LoadStages);
            loadStagesEvent.Name = StageConnectorNames.DataManager;
            loadStagesEvent.OnInvoke = HandleLoadStages;

            saveStageEvent = Connector.GetBindableEvent(StageConnectorNames.SaveStage);
            saveStageEvent.Name = StageConnectorNames.DataManager;
            saveStageEvent.OnInvoke = HandleSaveStage;

            removeStageEvent = Connector.GetBindableEvent(StageConnectorNames.RemoveStage);
            removeStageEvent.Name = StageConnectorNames.DataManager;
            removeStageEvent.OnInvoke = HandleRemoveStage;
        }

        private void HandleLoadStages(string senderName, dynamic? request)
        {
            try
            {
                if (request is not LoadStagesRequest)
                    throw new InvalidOperationException("잘못된 스테이지 로드 요청입니다.");

                if (!defaultStageInitialized)
                {
                    using Data data = new();
                    data.Database.EnsureCreated();
                    StageRepository.EnsureDefaultStage(data);
                    defaultStageInitialized = true;
                }

                List<StageData> stages = StageRepository.LoadStages();
                loadStagesEvent.Fire(senderName, StageEventResult.Succeeded(stages));
            }
            catch (Exception exception)
            {
                loadStagesEvent.Fire(senderName, StageEventResult.Failed(exception));
            }
        }

        private void HandleSaveStage(string senderName, dynamic? request)
        {
            try
            {
                if (request is not SaveStageRequest saveRequest)
                    throw new InvalidOperationException("잘못된 스테이지 저장 요청입니다.");

                StageRepository.SaveStage(saveRequest.Stage);
                saveStageEvent.Fire(senderName, StageEventResult.Succeeded());
            }
            catch (Exception exception)
            {
                saveStageEvent.Fire(senderName, StageEventResult.Failed(exception));
            }
        }

        private void HandleRemoveStage(string senderName, dynamic? request)
        {
            try
            {
                if (request is not RemoveStageRequest removeRequest)
                    throw new InvalidOperationException("잘못된 스테이지 삭제 요청입니다.");

                StageRepository.RemoveStage(removeRequest.StageId);
                removeStageEvent.Fire(senderName, StageEventResult.Succeeded());
            }
            catch (Exception exception)
            {
                removeStageEvent.Fire(senderName, StageEventResult.Failed(exception));
            }
        }
    }
}
