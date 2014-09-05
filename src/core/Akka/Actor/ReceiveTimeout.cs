using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Actor
{
    public class ReceiveTimeout : PossiblyHarmful
    {
        private ReceiveTimeout() { }
        private static readonly ReceiveTimeout _instance = new ReceiveTimeout();
        public static ReceiveTimeout Instance
        {
            get
            {
                return _instance;
            }
        }
    }
}
