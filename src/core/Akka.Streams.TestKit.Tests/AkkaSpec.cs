using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Xunit.Abstractions;

namespace Akka.Streams.TestKit.Tests
{
    public abstract class AkkaSpec : Akka.TestKit.Xunit2.TestKit, IWatchedByCoroner
    {
        public static readonly Config TestConfig = ConfigurationFactory.ParseString(@"
          akka {
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
          }")
            .WithFallback(ConfigurationFactory.FromResource<AkkaSpec>("Akka.Streams.TestKit.Tests.reference.conf"))
            .WithFallback(Akka.TestKit.Configs.TestConfigs.TestSchedulerConfig);

        protected AkkaSpec(ActorSystem system, ITestOutputHelper output = null) : base(system, output)
        {
        }

        protected AkkaSpec(Config config, ITestOutputHelper output = null) : this(ActorSystem.Create("test", ConfigurationFactory.Load().WithFallback(TestConfig)), output)
        {
        }

        protected AkkaSpec(string config, ITestOutputHelper output = null) : this(ConfigurationFactory.ParseString(config), output)
        {
        }

        protected AkkaSpec(ITestOutputHelper output = null) : this(TestConfig, output)
        {
        }


    }
}