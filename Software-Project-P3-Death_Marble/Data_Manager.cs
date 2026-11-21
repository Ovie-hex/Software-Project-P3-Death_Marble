using Force.DeepCloner;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;

namespace Software_Project_P3_Death_Marble
{
    internal class Data_Manager
    {
        public Data_Manager()
        {
            // Connector
            Connector.BindableEvent loadStageEv = Connector.GetBindableEvent("loadStageEv");
            Connector.BindableEvent saveStageEv = Connector.GetBindableEvent("saveStageEv");
            Connector.BindableEvent removeStageEv = Connector.GetBindableEvent("removeStageEv");


            Data data = new Data();
            data.Database.EnsureCreated();

            if (!data.Stages.Any())
            {
                List<StageData> initStages = [];

                data.Stages.AddRange(initStages);
                data.SaveChanges();
            }

            loadStageEv.OnInvoke = (string s, dynamic? d/*null*/) =>
            {              
                loadStageEv.Fire(s, data);
            };

            saveStageEv.OnInvoke = (string s, dynamic? d) =>
            {
                // 여기에서 들어온 데이터를 바탕으로 스테이지의 데이터를 변경해야함
                data.SaveChanges();
            };

            removeStageEv.OnInvoke = (string s, dynamic? d) =>
            {
                // 여기에서 들어온 스테이지의 uuid로 해당 uuid를 통해하여 지정된 스테이지를 삭제해야함
                data.SaveChanges();
            };

        }
    }
    

}
