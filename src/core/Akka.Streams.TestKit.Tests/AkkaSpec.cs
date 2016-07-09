//-----------------------------------------------------------------------
// <copyright file="AkkaSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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
        private static readonly bool IsDebug = false;
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
            if (IsDebug && output != null)
                Console.SetOut(new TestOutputHelperWriter(output));
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

#if CORECLR
            // TODO: CORECLR, why tey made this method as abstract?
            public override void Write(char value)
            {
                Write(value.ToString());
            }
#endif

            public override void Write(string value)
            {
                try
                {
                    _helper.WriteLine(value + "\b");
                }
                finally 
                {
                    base.Write(value);
                }
            }

            public override void WriteLine(string value)
            {
                try
                {
                    _helper.WriteLine(value);

                }
                finally
                {
                    base.WriteLine();
                }
            }
        }
    }
}
