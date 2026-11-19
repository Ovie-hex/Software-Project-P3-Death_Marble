using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;

namespace Software_Project_P3_Death_Marble
{
    internal class Data_Manager
    {
        public Data_Manager() {
            var c = Connector.getBindableEvent("a");
            c.fire(100);
        }
    }
}
