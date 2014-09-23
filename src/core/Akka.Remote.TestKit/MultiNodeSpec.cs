using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Configuration;

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

        internal RoleName MySelf
        {
            get { return _myself.Value; }
        }

        internal Config Config
        {
            get
            {
                //TODO: Equivalent in Helios?
                var transportConfig = _testTransport ? 
                    ConfigurationFactory.ParseString("akka.remote.netty.tcp.applied-adapters = [trttl, gremlin]")
                        :  ConfigurationFactory.Empty;

                var configs = ImmutableList.Create(_nodeConf[MySelf], _commonConf, transportConfig,
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

    public class MultiNodeSpec
    {
        public static int SelfIndex
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        public static Config NodeConfig
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        public static Config BaseConfig
        {
            get
            {
                throw new NotImplementedException();
            }
        }
    }
}
