using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;

namespace Software_Project_P3_Death_Marble
{
    internal class Stage_Customizer
    {
        // 여기에 M(MVVM) 정의할것
        public Stage_Customizer()
        {
            Connector.BindableEvent loadStageEv = Connector.GetBindableEvent("loadStageEv");
            loadStageEv.Name = "StageCustomizerEv";

            Connector.BindableEvent removeStageEv = Connector.GetBindableEvent("removeStageEv");
            Connector.BindableEvent loadCustromizer = Connector.GetBindableEvent("loadCustromizer");
        }
    }
}
