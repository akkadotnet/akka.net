using System;
using System.IO;
using System.Text;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Akka.TestKit.Internal.StringMatcher;
using Akka.TestKit.TestEvent;
using Akka.Util.Internal;
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
            .WithFallback(Akka.TestKit.Configs.TestConfigs.DefaultConfig)
            .WithFallback(ConfigurationFactory.FromResource<ActorMaterializer>("Akka.Streams.reference.conf"));

        protected AkkaSpec(ActorSystem system, ITestOutputHelper output = null) : base(system, output)
        {
#if DEBUG
            if (output != null)
                Console.SetOut(new TestOutputHelperWriter(output));
#endif
        }

        protected AkkaSpec(Config config, ITestOutputHelper output = null) : this(ActorSystem.Create("test", ConfigurationFactory.Load().WithFallback(config).WithFallback(TestConfig)), output)
        {
        }

        protected AkkaSpec(string config, ITestOutputHelper output = null) : this(ConfigurationFactory.ParseString(config), output)
        {
        }

        protected AkkaSpec(ITestOutputHelper output = null) : this(TestConfig, output)
        {
        }

        protected void MuteDeadLetters(params Type[] messageClasses)
        {
            if (!Sys.Log.IsDebugEnabled)
                return;

            Action<Type> mute =
                clazz =>
                    Sys.EventStream.Publish(
                        new Mute(new DeadLettersFilter(new PredicateMatcher(_ => true),
                            new PredicateMatcher(_ => true),
                            letter => clazz == typeof (object) || letter.Message.GetType() == clazz)));

            if (messageClasses.Length == 0)
                mute(typeof (object));
            else
                messageClasses.ForEach(mute);
        }

        private class TestOutputHelperWriter : TextWriter
        {
            private readonly ITestOutputHelper _helper;

            public TestOutputHelperWriter(ITestOutputHelper helper)
            {
                _helper = helper;
            }

            public override Encoding Encoding => Encoding.UTF8;

            public override void Write(string value)
            {
                _helper.WriteLine(value + "\b");
                base.Write(value);
            }

            public override void WriteLine(string value)
            {
                _helper.WriteLine(value);
                base.WriteLine();
            }
        }
    }
}
