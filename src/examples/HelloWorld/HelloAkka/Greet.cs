using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HelloAkka
{
    /// <summary>
    /// Immutable message type that actor will respond to
    /// </summary>
    public class Greet
    {
        public string Who { get; private set; }

        public Greet(string who)
        {
            Who = who;
        }
    }
}
