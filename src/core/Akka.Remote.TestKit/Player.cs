using System;
using System.Threading.Tasks;
using Helios.Topology;

namespace Akka.Remote.TestKit
{
    /// <summary>
    /// The Player is the client component of the
    /// test conductor extension. It registers with
    /// the conductor's controller
    ///  in order to participate in barriers and enable network failure injection
    /// </summary>
    partial class TestConductor //Player trait in JVM version
    {
        public Task<Done> StartClient(RoleName name, INode controllerAddr)
        {
            throw new NotImplementedException();
        }
    }
}
