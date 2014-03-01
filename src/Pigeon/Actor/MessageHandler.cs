using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Actor
{
    /// <summary>
    /// Delegate Receive
    /// </summary>
    /// <param name="message">The message.</param>
    public delegate void Receive(object message);
}
