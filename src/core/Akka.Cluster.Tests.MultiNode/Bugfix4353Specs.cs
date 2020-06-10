using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;

using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.TestKit;

namespace Akka.Cluster.Tests.MultiNode
{
    public class Bugfix4353Spec : MultiNodeClusterSpec
    {
        protected RoleName First;
        protected RoleName Second;
        protected RoleName Third;

        public Bugfix4353Spec() : this(new Bugfix4353SpecsConfig())
        {
        }

        protected Bugfix4353Spec(Bugfix4353SpecsConfig config) : base(config, typeof(Bugfix4353Spec))
        {
            First = config.First;
            Second = config.Second;
            Third = config.Third;
        }

        [MultiNodeFact]
        public void Bugfix4353Spec_Cluster_of_3_must_reach_cnovergence()
        {
            AwaitClusterUp(First, Second, Third);
            EnterBarrier("after-1");
        }
    }

    public class Bugfix4353SpecsConfig : MultiNodeConfig
    {
        private static readonly string[] Hocons =
        {
@"akka : {
  actor : {
    provider : cluster 
  }
}",

@"akka : {
  stdout-loglevel : INFO
  loglevel : INFO 
  log-config-on-start : on 
  loggers : [""Akka.Event.DefaultLogger""],
  actor : {
    debug : {
      receive : on
      autoreceive : on
      lifecycle : on
      event-stream : on
      unhandled : on 
    } 
  } 
}",

@"akka : {
  remote : {
    dot-netty : {
      tcp : {
        log-transport : true
        transport-class : ""Akka.Remote.Transport.DotNetty.TcpTransport, Akka.Remote""
        transport-protocol : tcp
        hostname : 0.0.0.0
        public-hostname : localhost
      } 
    } 
  } 
}",

@"akka : {
  cluster : {
    log-info : on
    seed-nodes : [
      ""akka.tcp://Bugfix4353Spec@localhost:5001"",
      ""akka.tcp://Bugfix4353Spec@localhost:5002"",
      ""akka.tcp://Bugfix4353Spec@localhost:5003""
    ]
    roles : [seed]
    role : { } 
  } 
}"
        };

        public static Config Config
        {
            get
            {
                var config = ConfigurationFactory.Empty;

                foreach (var hocon in Hocons)
                {
                    config = config.WithFallback(ConfigurationFactory.ParseString(hocon));
                }
                return config;
            }
        }

        public readonly RoleName First;
        public readonly RoleName Second;
        public readonly RoleName Third;

        public Bugfix4353SpecsConfig()
        {

            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = MultiNodeClusterSpec.ClusterConfig(false);

            NodeConfig(
                new[] { First },
                new[] {
                    ConfigurationFactory
                    .ParseString("akka.remote.dot-netty.tcp.port : 5001")
                    .WithFallback(Config)
                });

            NodeConfig(
                new[] { Second },
                new[] {
                    ConfigurationFactory
                    .ParseString("akka.remote.dot-netty.tcp.port : 5002")
                    .WithFallback(Config)
                });

            NodeConfig(
                new[] { Third },
                new[] {
                    ConfigurationFactory
                    .ParseString("akka.remote.dot-netty.tcp.port : 5003")
                    .WithFallback(Config)
                });

        }
    }
}
