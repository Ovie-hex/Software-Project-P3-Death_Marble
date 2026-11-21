using System;
using System.Collections.Generic;
using System.Text;

namespace Software_Project_P3_Death_Marble
{
    internal class Game_Manager
    {
        // 여기에 M(MVVM) 정의할것
        public Game_Manager()
        {
            Connector.BindableEvent loadStageEv = Connector.GetBindableEvent("loadStageEv");
            loadStageEv.Name = "GameManagerrEv";

            Connector.BindableEvent loadGame = Connector.GetBindableEvent("loadGame");
        }
    }
}
