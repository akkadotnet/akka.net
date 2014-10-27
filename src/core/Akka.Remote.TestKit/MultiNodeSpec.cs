using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Configuration.Hocon;
using Akka.Event;
using Akka.TestKit;
using Akka.Util.Internal;
using Helios.Topology;

namespace Akka.Remote.TestKit
{
    /// <summary>
    /// Configure the role names and participants of the test, including configuration settings
    /// </summary>
    public abstract class MultiNodeConfig
    {
        Config _commonConf = null;
        ImmutableDictionary<RoleName, Config> _nodeConf = ImmutableDictionary.Create<RoleName, Config>();
        ImmutableList<RoleName> _roles = ImmutableList.Create<RoleName>();
        ImmutableDictionary<RoleName, ImmutableList<string>> _deployments = ImmutableDictionary.Create<RoleName, ImmutableList<string>>();
        ImmutableList<string> _allDeploy = ImmutableList.Create<string>();
        bool _testTransport = false;

        /// <summary>
        /// Register a common base config for all test participants, if so desired.
        /// </summary>
        public Config CommonConfig
        {
            set { _commonConf = value; }
        }

        /// <summary>
        /// Register a config override for a specific participant.
        /// </summary>
        public void NodeConfig(IEnumerable<RoleName> roles, IEnumerable<Config> configs)
        {
            var c = configs.Aggregate((a, b) => a.WithFallback(b));
            _nodeConf = _nodeConf.AddRange(roles.Select(r => new KeyValuePair<RoleName, Config>(r, c)));
        }

        /// <summary>
        /// Include for verbose debug logging
        /// </summary>
        /// <param name="on">when `true` debug Config is returned, otherwise config with info logging</param>
        public Config DebugConfig(bool on)
        {
            if (on)
                return ConfigurationFactory.ParseString(@"
                    akka.loglevel = DEBUG
                    akka.remote {
                        log-received-messages = on
                        log-sent-messages = on
                    }
                    akka.actor.debug {
                        receive = on
                        fsm = on
                    }
                    akka.remote.log-remote-lifecycle-events = on
                ");
            return ConfigurationFactory.Empty;
        }

        public RoleName Role(string name)
        {
            if(_roles.Exists(r => r.Name == name)) throw new ArgumentException("non-unique role name " + name);
            var roleName = new RoleName(name);
            _roles = _roles.Add(roleName);
            return roleName;
        }

        public void DeployOn(RoleName role, string deployment)
        {
            ImmutableList<string> roleDeployments;
            _deployments.TryGetValue(role, out roleDeployments);
            _deployments = _deployments.SetItem(role,
                roleDeployments == null ? ImmutableList.Create(deployment) : roleDeployments.Add(deployment));
        }

        public void DeployOnAll(string deployment)
        {
            _allDeploy = _allDeploy.Add(deployment);
        }

        /// <summary>
        /// To be able to use `blackhole`, `passThrough`, and `throttle` you must
        /// activate the failure injector and throttler transport adapters by
        /// specifying `testTransport(on = true)` in your MultiNodeConfig.
        /// </summary>
        public bool TestTransport
        {
            set { _testTransport = value; }
        }

        readonly Lazy<RoleName> _myself;

        protected MultiNodeConfig()
        {
            _myself = new Lazy<RoleName>(() =>
            {
                if(_roles.Count > MultiNodeSpec.SelfIndex) throw new ArgumentException("not enough roles declared for this test");
                return _roles[MultiNodeSpec.SelfIndex];
            });
        }

        public RoleName Myself
        {
            get { return _myself.Value; }
        }

        internal Config Config
        {
            get
            {
                //TODO: Equivalent in Helios?
                var transportConfig = _testTransport ? 
                    ConfigurationFactory.ParseString("akka.remote.helios.tcp.applied-adapters = [trttl, gremlin]")
                        :  ConfigurationFactory.Empty;

                var configs = ImmutableList.Create(_nodeConf[Myself], _commonConf, transportConfig,
                    MultiNodeSpec.NodeConfig, MultiNodeSpec.BaseConfig);

                return configs.Aggregate((a, b) => a.WithFallback(b));
            }
        }

        internal ImmutableList<string> Deployments(RoleName node)
        {
            ImmutableList<string> deployments;
            _deployments.TryGetValue(node, out deployments);
            return deployments == null ? _allDeploy : deployments.AddRange(_allDeploy);
        }

        internal ImmutableList<RoleName> Roles
        {
            get { return _roles; }
        }
    }

    //TODO: Applicable?
    /// <summary>
    /// Note: To be able to run tests with everything ignored or excluded by tags
    /// you must not use `testconductor`, or helper methods that use `testconductor`,
    /// from the constructor of your test class. Otherwise the controller node might
    /// be shutdown before other nodes have completed and you will see errors like:
    /// `AskTimeoutException: sending to terminated ref breaks promises`. Using lazy
    /// val is fine.
    /// </summary>
    public class MultiNodeSpec : TestKitBase, IMultiNodeSpecCallbacks
    {
        //TODO: Sort out references to Java classes in 

        /// <summary>
        /// Number of nodes node taking part in this test.
        /// -Dmultinode.max-nodes=4
        /// </summary>
        public static int MaxNodes {get{ throw new NotImplementedException();}}

        /// <summary>
        /// Name (or IP address; must be resolvable)
        /// of the host this node is running on
        /// 
        /// <code>-Dmultinode.host=host.example.com</code>
        /// 
        /// InetAddress.getLocalHost.getHostAddress is used if empty or "localhost"
        /// is defined as system property "multinode.host".
        /// </summary>
        public static string SelfName { get { throw new NotImplementedException(); } }

        //TODO: require(selfName != "", "multinode.host must not be empty")

        /// <summary>
        /// Port number of this node. Defaults to 0 which means a random port.
        /// 
        /// <code>-Dmultinode.port=0</code>
        /// </summary>
        public static int SelfPort { get { throw new NotImplementedException(); } }

        //TODO: require(selfPort >= 0 && selfPort < 65535, "multinode.port is out of bounds: " + selfPort)

        /// <summary>
        /// Name (or IP address; must be resolvable using InetAddress.getByName)
        /// of the host that the server node is running on.
        /// 
        /// <code>-Dmultinode.server-host=server.example.com</code>
        /// </summary>
        public static string ServerName { get { throw new NotImplementedException(); } }

        //TODO: require(serverName != "", "multinode.server-host must not be empty")

        /// <summary>
        /// Port number of the node that's running the server system. Defaults to 4711.
        /// 
        /// <code>-Dmultinode.server-port=4711</code>
        /// </summary>
        public static int ServerPort { get { throw new NotImplementedException(); } }

        //TODO: require(serverPort > 0 && serverPort < 65535, "multinode.server-port is out of bounds: " + serverPort)
        
        /// <summary>
        /// Index of this node in the roles sequence. The TestConductor
        /// is started in “controller” mode on selfIndex 0, i.e. there you can inject
        /// failures and shutdown other nodes etc.
        /// </summary>
        public static int SelfIndex { get { throw new NotImplementedException(); } }

        //TODO: require(selfIndex >= 0 && selfIndex < maxNodes, "multinode.index is out of bounds: " + selfIndex)

        public static Config NodeConfig
        {
            get
            {
                const string config = @"
                akka.actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                helios.tcp.hostname = ""{0}""
                helios.tcp.port = {1}";

                return ConfigurationFactory.ParseString(String.Format(config, SelfName, SelfPort));
            }
        }

        public static Config BaseConfig
        {
            get
            {
                return ConfigurationFactory.ParseString(
                      @"akka {
                        loglevel = ""WARNING""
                        stdout-loglevel = ""WARNING""
                        actor {
                          default-dispatcher {
                            executor = ""fork-join-executor""
                            fork-join-executor {
                              parallelism-min = 8
                              parallelism-factor = 2.0
                              parallelism-max = 8
                            }
                          }
                        }
                      }").WithFallback(TestKitBase.DefaultConfig);
            }
        }

        private static string GetCallerName()
        {
            var @this = typeof(MultiNodeSpec).Name;
            var trace = new StackTrace();
            var frames = trace.GetFrames();
            if (frames != null)
            {
                for (var i = 1; i < frames.Length; i++)
                {
                    var t = frames[i].GetMethod().DeclaringType;
                    if (t != null && t.Name != @this) return t.Name;
                }
            }
            throw new InvalidOperationException("Unable to find calling type");
        }

        readonly RoleName _myself;
        public RoleName Myself { get { return _myself; } }
        readonly LoggingAdapter _log;
        readonly ImmutableList<RoleName> _roles;
        readonly Func<RoleName, ImmutableList<string>> _deployments;
        readonly ImmutableDictionary<RoleName, Replacement> _replacements;
        readonly Address _myAddress;

        public MultiNodeSpec(TestKitAssertions assertions, MultiNodeConfig config) :
            this(assertions, config.Myself, ActorSystem.Create(GetCallerName(), config.Config), config.Roles, config.Deployments)
        {   
        }

        public MultiNodeSpec(
            TestKitAssertions assertions, 
            RoleName myself, 
            ActorSystem system, 
            ImmutableList<RoleName> roles, 
            Func<RoleName, ImmutableList<string>> deployments) : base(assertions, system)
        {
            _myself = myself;
            _log = Logging.GetLogger(Sys, this);
            _roles = roles;
            _deployments = deployments;
            _controllerAddr = Helios.Topology.Node.FromString(String.Format("{0}:{1}", ServerName, ServerPort));
            _replacements = _roles.ToImmutableDictionary(r => r, r => new Replacement("@" + r.Name + "@", r, this));

            InjectDeployments(system, myself);

            _myAddress = system.AsInstanceOf<ExtendedActorSystem>().Provider.DefaultAddress;

            Log.Info("Role [{0}] started with address [{1}]", myself.Name, _myAddress);
        }

        public void MultiNodeSpecBeforeAll()
        {
            AtStartup();
        }

        public void MultiNodeSpecAfterAll()
        {
            // wait for all nodes to remove themselves before we shut the conductor down
            if (SelfIndex == 0)
            {
                _testConductor.RemoveNode(_myself);
                //TODO: Async stuff here
                AwaitCondition(() => _testConductor.GetNodes().Result.Any(n => !n.Equals(_myself))
                    , _testConductor.Settings.BarrierTimeout);
            }
            Shutdown(Sys);
            AfterTermination();
        }

        protected virtual TimeSpan ShutdownTimeout { get{return TimeSpan.FromSeconds(5);} }

        /// <summary>
        /// Override this and return `true` to assert that the
        /// shutdown of the `ActorSystem` was done properly.
        /// </summary>
        protected virtual bool VerifySystemShutdown{get { return false; }}

        //Test Class Interface

        /// <summary>
        /// Override this method to do something when the whole test is starting up.
        /// </summary>
        protected virtual void AtStartup()
        {
        }

        /// <summary>
        /// Override this method to do something when the whole test is terminating.
        /// </summary>
        protected virtual void AfterTermination()
        {
        }

        /// <summary>
        /// All registered roles
        /// </summary>
        public ImmutableList<RoleName> Roles { get { return _roles; } }

        public virtual int InitialParticipants { get { throw new NotImplementedException();} }

        //TODO: require(initialParticipants > 0, "initialParticipants must be a 'def' or early initializer, and it must be greater zero")
        //TODO: require(initialParticipants <= maxNodes, "not enough nodes to run this test")

        TestConductor _testConductor;

        /// <summary>
        /// Execute the given block of code only on the given nodes (names according
        /// to the `roleMap`).
        /// </summary>
        public void RunOn(Action thunk, params RoleName[] nodes)
        {
            if (IsNode(nodes)) thunk();
        }

        /// <summary>
        /// Verify that the running node matches one of the given nodes
        /// </summary>
        public bool IsNode(params RoleName[] nodes)
        {
            return nodes.Contains(_myself);
        }

        /// <summary>
        /// Enter the named barriers in the order given. Use the remaining duration from
        /// the innermost enclosing `within` block or the default `BarrierTimeout`
        /// </summary>
        public void EnterBarrier(params string[] name)
        {
            _testConductor.Enter(RemainingOr(_testConductor.Settings.BarrierTimeout), name.ToImmutableList());
        }

        /// <summary>
        /// Query the controller for the transport address of the given node (by role name) and
        /// return that as an ActorPath for easy composition:
        /// 
        /// <code>var serviceA = Sys.ActorSelection(Node(new RoleName("master")) / "user" / "serviceA");</code>
        /// </summary>
        public ActorPath Node(RoleName role)
        {
            //TODO: Async stuff here 
            return new RootActorPath(_testConductor.GetAddressFor(role).Result);
        }

        public void MuteDeadLetters(ActorSystem system = null, params Type[] messageClasses)
        {
            if (system == null) system = Sys;
            if (!system.Log.IsDebugEnabled)
            {
                if (messageClasses.Any())
                    foreach (var @class in messageClasses) EventFilter.DeadLetter(@class).Mute();
                else EventFilter.DeadLetter(typeof(object)).Mute();
            }
        }

        /*
        * Implementation (i.e. wait for start etc.)
        */

        readonly INode _controllerAddr;

        protected void AttachConductor(TestConductor tc)
        {
            var timeout = tc.Settings.BarrierTimeout;
            try
            {
                //TODO: Async stuff
                if(SelfIndex == 0)
                    tc.StartController(InitialParticipants, _myself, _controllerAddr).Wait(timeout);
                else
                    tc.StartClient(_myself, _controllerAddr).Wait(timeout);
            }
            catch (Exception e)
            {
                throw new Exception("failure while attaching new conductor", e);
            }
            _testConductor = tc;
        }

        // now add deployments, if so desired

        sealed class Replacement
        {
            readonly string _tag;
            public string Tag { get { return _tag; } }
            readonly RoleName _role;
            public RoleName Role { get { return _role; } }
            readonly Lazy<string> _addr;
            public string Addr { get { return _addr.Value; } }

            public Replacement(string tag, RoleName role, MultiNodeSpec spec)
            {
                _tag = tag;
                _role = role;
                _addr = new Lazy<string>(() => spec.Node(role).Address.ToString());
            }
        }
        
        protected void InjectDeployments(ActorSystem system, RoleName role)
        {
            var deployer = Sys.AsInstanceOf<ExtendedActorSystem>().Provider.Deployer;
            foreach (var str in _deployments(role))
            {
                var deployString = _replacements.Values.Aggregate(str, (@base, r) =>
                {
                    var indexOf = @base.IndexOf(r.Tag, StringComparison.Ordinal);
                    if (indexOf == -1) return @base;
                    string replaceWith;
                    try
                    {
                        replaceWith = r.Addr;
                    }
                    catch(Exception e)
                    {
                        // might happen if all test cases are ignored (excluded) and
                        // controller node is finished/exited before r.addr is run
                        // on the other nodes
                        var unresolved = "akka://unresolved-replacement-" + r.Role.Name;
                        Log.Warning(unresolved + " due to: " + e.ToString());
                        replaceWith = unresolved;
                    }
                    return @base.Replace(r.Tag, replaceWith);
                });
                foreach (var pair in ConfigurationFactory.ParseString(deployString).AsEnumerable())
                {
                    if (pair.Value.IsObject())
                    {
                        var deploy =
                            deployer.ParseConfig(pair.Key, new Config(new HoconRoot(pair.Value)));
                        deployer.SetDeploy(deploy);
                    }
                    else
                    {
                        throw new ArgumentException(String.Format("key {0} must map to deployment section, not simple value {1}", 
                            pair.Key, pair.Value));
                    }
                }
            }
        }

        protected ActorSystem StartNewSystem()
        {
            var config =
                ConfigurationFactory
                .ParseString(String.Format(@"helios.tcp{port={0}\nhostname=""{1}""", 
                    _myAddress.Host,
                    _myAddress.Port))
                .WithFallback(Sys.Settings.Config);

            var system = ActorSystem.Create(Sys.Name, config);
            InjectDeployments(system, _myself);
            AttachConductor(new TestConductor(system));
            return system;
        }
    }

    //TODO: Improve docs
    /// <summary>
    /// Use this to hook MultiNodeSpec into your test framework lifecycle
    /// </summary>
    interface IMultiNodeSpecCallbacks
    {
        /// <summary>
        /// Call this before the start of the test run. NOT before every test case.
        /// </summary>
        void MultiNodeSpecBeforeAll();

        /// <summary>
        /// Call this after the all test cases have run. NOT after every test case.
        /// </summary>
        void MultiNodeSpecAfterAll();
    }
}
