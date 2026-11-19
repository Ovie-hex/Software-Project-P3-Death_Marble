using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;

namespace Software_Project_P3_Death_Marble
{
    internal class Stage_Customizer
    {
        public Stage_Customizer() {
            var c = Connector.getBindableEvent("a");
            c.name = "ff";
            c.onInvoke = (string n, dynamic d) => {
                MessageBox.Show(Convert.ToString(d));
            };
        }
    }
}
