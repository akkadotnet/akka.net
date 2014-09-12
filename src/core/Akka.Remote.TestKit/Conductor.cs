using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Pattern;
using Akka.Remote.Transport;
using Akka.Util;

namespace Akka.Remote.TestKit
{
    /// <summary>
    /// The conductor is the one orchestrating the test: it governs the
    /// <see cref="Akka.Remote.TestKit.Controller">'s ports to which all
    /// Players connect, it issues commands to their
    /// <see cref="Akka.Remote.TestKit.NetworkFailureInjector"></see> and provides support
    /// for barriers using the <see cref="Akka.Remote.TestKit.BarrierCoordinator"></see>.
    /// All of this is bundled inside the <see cref="TestConductor"/>
    /// </summary>
    partial class TestConductor //Conductor trait in JVM version
    {
        ActorRef _controller;

        public ActorRef Controller
        {
            get
            {
                if(_controller == null) throw new IllegalStateException("TestConductorServer was not started");
                return _controller;
            }
        }


        public void StartController()
        {
            //TODO: This will be different if using app domains and remoting. Need to have more of a look at the code to work out if that is the right direction
            throw new NotImplementedException();
        }

        /// <summary>
        /// Make the remoting pipeline on the node throttle data sent to or received
        /// from the given remote peer. Throttling works by delaying packet submission
        /// within the netty pipeline until the packet would have been completely sent
        /// according to the given rate, the previous packet completion and the current
        /// packet length. In case of large packets they are split up if the calculated
        /// end pause would exceed `akka.testconductor.packet-split-threshold`
        /// (roughly). All of this uses the system’s scheduler, which is not
        /// terribly precise and will execute tasks later than they are schedule (even
        /// on average), but that is countered by using the actual execution time for
        /// determining how much to send, leading to the correct output rate, but with
        /// increased latency.
        /// 
        /// ====Note====
        /// To use this feature you must activate the failure injector and throttler
        /// transport adapters by specifying `testTransport(on = true)` in your MultiNodeConfig.
        /// </summary>
        /// <param name="node">is the symbolic name of the node which is to be affected</param>
        /// <param name="target">is the symbolic name of the other node to which connectivity shall be throttled</param>
        /// <param name="direction">can be either `Direction.Send`, `Direction.Receive` or `Direction.Both`</param>
        /// <param name="rateMBit">is the maximum data rate in MBit</param>
        /// <returns></returns>
        public Task<Done> Throttle(RoleName node, RoleName target, ThrottleTransportAdapter.Direction direction,
            float rateMBit)
        {
            RequireTestConductorTransport();
            return Controller.Ask<Done>(new Throttle(node, target, direction, rateMBit));
        }

        /// <summary>
        /// Switch the helios pipeline of the remote support into blackhole mode for
        /// sending and/or receiving: it will just drop all messages right before
        /// submitting them to the Socket or right after receiving them from the
        /// Socket.
        /// 
        ///  ====Note====
        /// To use this feature you must activate the failure injector and throttler
        /// transport adapters by specifying `testTransport(on = true)` in your MultiNodeConfig.
        /// </summary>
        /// <param name="node">is the symbolic name of the node which is to be affected</param>
        /// <param name="target">is the symbolic name of the other node to which connectivity shall be impeded</param>
        /// <param name="direction">can be either `Direction.Send`, `Direction.Receive` or `Direction.Both`</param>
        /// <returns></returns>
        public Task<Done> Blackhole(RoleName node, RoleName target, ThrottleTransportAdapter.Direction direction)
        {
            return Throttle(node, target, direction, 0f);
        }

        private void RequireTestConductorTransport()
        {
            if(!Transport.DefaultAddress.Protocol.Contains(".trttl.gremlin."))
                throw new ConfigurationException("To use this feature you must activate the failure injector adapters " +
                    "(trttl, gremlin) by specifying `testTransport(on = true)` in your MultiNodeConfig.");
        }

        /// <summary>
        /// Switch the Helios pipeline of the remote support into pass through mode for
        /// sending and/or receiving.
        /// 
        /// ====Note====
        /// To use this feature you must activate the failure injector and throttler
        /// transport adapters by specifying `testTransport(on = true)` in your MultiNodeConfig.
        /// </summary>
        /// <param name="node">is the symbolic name of the node which is to be affected</param>
        /// <param name="target">is the symbolic name of the other node to which connectivity shall be impeded</param>
        /// <param name="direction">can be either `Direction.Send`, `Direction.Receive` or `Direction.Both`</param>
        /// <returns></returns>
        public Task<Done> PassThrough(RoleName node, RoleName target, ThrottleTransportAdapter.Direction direction)
        {
            return Throttle(node, target, direction, -1f);
        }

        /// <summary>
        /// Tell the remote support to TCP_RESET the connection to the given remote
        /// peer. It works regardless of whether the recipient was initiator or
        /// responder.
        /// </summary>
        /// <param name="node">is the symbolic name of the node which is to be affected</param>
        /// <param name="target">is the symbolic name of the other node to which connectivity shall be impeded</param>
        /// <returns></returns>
        public Task<Done> Disconnect(RoleName node, RoleName target)
        {
            return Controller.Ask<Done>(new Disconnect(node, target, false));
        }

        /// <summary>
        /// Tell the remote support to TCP_RESET the connection to the given remote
        /// peer. It works regardless of whether the recipient was initiator or
        /// responder.
        /// </summary>
        /// <param name="node">is the symbolic name of the node which is to be affected</param>
        /// <param name="target">is the symbolic name of the other node to which connectivity shall be impeded</param>
        /// <returns></returns>
        public Task<Done> Abort(RoleName node, RoleName target)
        {
            return Controller.Ask<Done>(new Disconnect(node, target, true));
        }

        /// <summary>
        /// Tell the actor system at the remote node to shut itself down. The node will also be
        /// removed, so that the remaining nodes may still pass subsequent barriers.
        /// </summary>
        /// <param name="node">is the symbolic name of the node which is to be affected</param>
        /// <param name="exitValue">is the return code which shall be given to System.exit</param>
        /// <returns></returns>
        public Task<Done> Exit(RoleName node, int exitValue)
        {
            // the recover is needed to handle ClientDisconnectedException exception,
            // which is normal during shutdown
            try
            {
                return Controller.Ask<Done>(new Terminate(node, new Right<bool, int>(exitValue)));
            }
            catch (Controller.ClientDisconnectedException)
            {
                return Task.FromResult(Done.Instance);
            }
        }

        /// <summary>
        /// Tell the actor system at the remote node to shut itself down without
        /// awaiting termination of remote-deployed children. The node will also be
        /// removed, so that the remaining nodes may still pass subsequent barriers.
        /// </summary>
        /// <param name="node">is the symbolic name of the node which is to be affected</param>
        /// <param name="abort"></param>
        /// <returns></returns>
        public Task<Done> Shutdown(RoleName node, bool abort = false)
        {
            // the recover is needed to handle ClientDisconnectedException exception,
            // which is normal during shutdown
            try
            {
                return Controller.Ask<Done>(new Terminate(node, new Left<bool, int>(abort)));
            }
            catch (Controller.ClientDisconnectedException)
            {
                return Task.FromResult(Done.Instance);
            }
        }

        /// <summary>
        /// Obtain the list of remote host names currently registered.
        /// </summary>
        public Task<IEnumerable<RoleName>> GetNodes()
        {
            return Controller.Ask<IEnumerable<RoleName>>(TestKit.Controller.GetNodes.Instance);
        }

        /// <summary>
        /// Remove a remote host from the list, so that the remaining nodes may still
        /// pass subsequent barriers. This must be done before the client connection
        /// breaks down in order to affect an “orderly” removal (i.e. without failing
        /// present and future barriers).
        /// </summary>
        /// <param name="node">is the symbolic name of the node which is to be removed</param>
        /// <returns></returns>
        public Task<Done> RemoveNode(RoleName node)
        {
            return Controller.Ask<Done>(new Remove(node));
        }
    }

    //TODO: Remoting here? Not sure this is right
    class ConductorHandler
    {
        public ConductorHandler()
        {
            throw new NotImplementedException();
        }
    }

    class ServerFSM
    {
        public ServerFSM()
        {
            throw new NotImplementedException();
        }
    }
}
