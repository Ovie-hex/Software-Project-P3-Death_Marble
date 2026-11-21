using System;
using System.Collections.Generic;
using System.Text;

namespace Software_Project_P3_Death_Marble
{
    internal class XAML_Manager
    {
        // MainWindow 창의 현재 상태
        /*
         * 현재 창의 크기, 마우스 위치 등
         */

        //여기에 VM(MVVM) 정의할것

        public XAML_Manager()
        {
            // 여기에 Connector.BindableEvent 정의
            Connector.BindableEvent loadGame = Connector.GetBindableEvent("loadGame");
            Connector.BindableEvent loadCustromizer = Connector.GetBindableEvent("loadCustromizer");

            // 각 이벤트의 OnInvoke 정의
            loadGame.OnInvoke = (string s, dynamic? d) =>
            {

            };

            loadCustromizer.OnInvoke = (string s, dynamic? d) =>
            {

            };

            // Xaml 초기 세팅 (여기에서 MainWindow에 삽입할 XAML들 생성)
        }
    }
}
