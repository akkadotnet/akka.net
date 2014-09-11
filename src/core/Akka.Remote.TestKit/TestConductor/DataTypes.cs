using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Remote.TestKit.TestConductor
{
    public sealed class RoleName
    {
        public RoleName(string name)
        {
            Name = name;
        }

        public string Name { get; private set; }
    }

    /// <summary>
    /// Messages sent from <see cref="Conductor"/> to <see cref="Player"/>
    /// </summary>
    public interface ClientOp { } 
}
