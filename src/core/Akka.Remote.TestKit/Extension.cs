using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Util.Internal;

namespace Akka.Remote.TestKit
{
    /// <summary>
    /// Access to the <see cref="TestConductor"></see> extension:
    /// 
    /// {{{
    /// var tc = TestConductor(system)
    /// tc.StartController(numPlayers)
    /// OR
    /// tc.StartClient(conductorPort)
    /// }}}
    /// </summary>
    public class TestConductorExtension : ExtensionIdProvider<TestConductor>
    {
        //TODO:
        //override def lookup = TestConductor

        public override TestConductor CreateExtension(ExtendedActorSystem system)
        {
            return new TestConductor(system);
        }

        //TODO:
        //override def get(system: ActorSystem): TestConductorExt = super.get(system)
        //def apply()(implicit ctx: ActorContext): TestConductorExt = apply(ctx.system)
    }

    /// <summary>
    /// This binds together the Conductor and Player in an extension
    /// ====Note====
    /// This extension requires the `akka.actor.provider`
    /// to be a <see cref="Akka.Remote.RemoteActorRefProvider"/>.
    /// To use ``blackhole``, ``passThrough``, and ``throttle`` you must activate the
    /// failure injector and throttler transport adapters by specifying `testTransport(on = true)`
    /// in your MultiNodeConfig.
    /// </summary>
    public partial class TestConductor : IExtension
    {
        public static TestConductor Get(ActorSystem system)
        {
            return system.WithExtension<TestConductor, TestConductorExtension>();
        }

        readonly TestConductorSettings _settings;
        public TestConductorSettings Settings {get { return _settings; }}

        readonly RemoteTransport _transport;
        /// <summary>
        /// Remote transport used by the actor ref provider.
        /// </summary>
        public RemoteTransport Transport { get { return _transport; } }

        readonly Address _address;
        /// <summary>
        /// Transport address of this Helios-like remote transport.
        /// </summary>
        public Address Address { get { return _address; } }

        readonly ExtendedActorSystem _system;

        public TestConductor(ExtendedActorSystem system)
        {
            _settings = new TestConductorSettings(system.Settings.Config.GetConfig("akka.testconductor"));
            _transport = system.Provider.AsInstanceOf<RemoteActorRefProvider>().Transport;
            _address = _transport.DefaultAddress;
            _system = system;
        }
    }

    public class TestConductorSettings
    {
        readonly TimeSpan _connectTimeout;
        public TimeSpan ConnectTimeout { get { return _connectTimeout; } }

        readonly int _clientReconnects;
        public int ClientReconnects { get { return _clientReconnects; } }

        readonly TimeSpan _reconnectBackoff;
        public TimeSpan ReconnectBackoff { get { return _reconnectBackoff; } }

        readonly TimeSpan _barrierTimeout;
        public TimeSpan BarrierTimeout { get { return _barrierTimeout; } }

        readonly TimeSpan _queryTimeout;
        public TimeSpan QueryTimeout { get { return _queryTimeout; } }

        readonly TimeSpan _packetSplitThreshold;
        public TimeSpan PacketSplitThreshold { get { return _packetSplitThreshold; } }

        private readonly int _serverSocketWorkerPoolSize;
        public int ServerSocketWorkerPoolSize{ get { return _serverSocketWorkerPoolSize; } }

        private readonly int _clientSocketWorkerPoolSize;
        public int ClientSocketWorkerPoolSize { get { return _clientSocketWorkerPoolSize; } }

        public TestConductorSettings(Config config)
        {
            _connectTimeout = config.GetTimeSpan("connect-timeout");
            _clientReconnects = config.GetInt("client-reconnects");
            _reconnectBackoff = config.GetTimeSpan("reconnect-backoff");
            _barrierTimeout = config.GetTimeSpan("barrier-timeout");
            _queryTimeout = config.GetTimeSpan("query-timeout");
            _packetSplitThreshold = config.GetTimeSpan("packet-split-threshold");
            //_serverSocketWorkerPoolSize = ComputeWps(config.GetConfig("helios.tcp.server-socket-worker-pool"));
            //_clientSocketWorkerPoolSize = ComputeWps(config.GetConfig("helios.tcp.client-socket-worker-pool"));
        }

        public int ComputeWps(Config config)
        {
            return ThreadPoolConfig.ScaledPoolSize(
                config.GetInt("pool-size-min"),
                config.GetDouble("pool-size-factor"),
                config.GetInt("pool-size-max"));
        }
    }
}
