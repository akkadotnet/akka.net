#region copyright
// -----------------------------------------------------------------------
//  <copyright file="BackwardCompatibilitySpec.cs" company="Akka.NET project">
//      Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2017 Akka.NET project <https://github.com/akkadotnet>
//  </copyright>
// -----------------------------------------------------------------------
#endregion

#if HELIOS
using System;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Transport.DotNetty;
using Akka.TestKit;
using Helios.Logging;
using Xunit;
using Xunit.Abstractions;
using Debug = Helios.Logging.Debug;
using Error = Helios.Logging.Error;
using Info = Helios.Logging.Info;
using LogLevel = Helios.Logging.LogLevel;
using Warning = Helios.Logging.Warning;

namespace Akka.Remote.Tests.Transport
{
    public class BackwardCompatibilitySpec : AkkaSpec
    {
        private static readonly Config TestConfig = ConfigurationFactory.ParseString(@"
            akka {
                actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""

                remote.helios.tcp {
                    hostname = ""localhost""
                    port = 11311
                }
            }");
        

        public BackwardCompatibilitySpec(ITestOutputHelper output) : base(TestConfig, output)
        {
        }

        [Fact]
        public void DotNetty_transport_can_fallback_to_helios_settings()
        {
            var remoteActorRefProvider = RARP.For(Sys).Provider;
            var remoteSettings = remoteActorRefProvider.RemoteSettings;

            Assert.Equal(typeof(TcpTransport), Type.GetType(remoteSettings.Transports.First().TransportClass));
            Assert.Equal("localhost", remoteActorRefProvider.DefaultAddress.Host);
            Assert.Equal(11311, remoteActorRefProvider.DefaultAddress.Port.Value);
        }

        [Fact]
        public void DotNetty_transport_can_communicate_with_Helios_transport()
        {
            LoggingFactory.DefaultFactory = new XunitLoggingFactory(Output);
            var heliosConfig = ConfigurationFactory.ParseString(@"
                akka {
                    actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""

                    remote {
                        enabled-transports = [""akka.remote.helios.tcp""]
                        helios.tcp {
                            hostname = ""localhost""
                            port = 11223
                        }
                    }
                }");

            using (var heliosSystem = ActorSystem.Create("helios-system", heliosConfig))
            {
                InitializeLogger(heliosSystem);
                heliosSystem.ActorOf(Props.Create<Echo>(), "echo");

                var heliosProvider = RARP.For(heliosSystem).Provider;

                Assert.Equal(
                    "Akka.Remote.Transport.Helios.HeliosTcpTransport, Akka.Remote.Transport.Helios", 
                    heliosProvider.RemoteSettings.Transports.First().TransportClass);
                
                var address = heliosProvider.DefaultAddress;

                Assert.Equal(11223, address.Port.Value);
                
                var echo = Sys.ActorSelection(new RootActorPath(address) / "user" / "echo");
                echo.Tell("hello", TestActor);
                ExpectMsg("hello");
            }
        }

        #region helper classes

        private sealed class Echo : ReceiveActor
        {
            public Echo()
            {
                ReceiveAny(msg => Sender.Tell(msg));
            }
        }

        private sealed class XunitLogger : LoggingAdapter
        {
            private readonly ITestOutputHelper _output;

            public XunitLogger(ITestOutputHelper output, string logSource, params LogLevel[] supportedLogLevels)
                : base(logSource, supportedLogLevels)
            {
                _output = output;
            }

            protected override void DebugInternal(Debug message) => 
                _output.WriteLine($"[DEBUG][{message.Timestamp}][{message.ThreadId}][{message.LogSource}] {message.Message}");

            protected override void InfoInternal(Info message) =>
                _output.WriteLine($"[INFO][{message.Timestamp}][{message.ThreadId}][{message.LogSource}] {message.Message}");

            protected override void WarningInternal(Warning message) =>
                _output.WriteLine(message.Cause != null 
                    ? $"[WARNING][{message.Timestamp}][{message.ThreadId}][{message.LogSource}] {message.Message} Caused by: {message.Cause.Message} {message.Cause.StackTrace}"
                    : $"[WARNING][{message.Timestamp}][{message.ThreadId}][{message.LogSource}] {message.Message}");

            protected override void ErrorInternal(Error message) =>
                _output.WriteLine($"[ERROR][{message.Timestamp}][{message.ThreadId}][{message.LogSource}] {message.Message} Caused by: {message.Cause.Message} {message.Cause.StackTrace}");
        }

        private sealed class XunitLoggingFactory : LoggingFactory
        {
            private readonly ITestOutputHelper _output;

            public XunitLoggingFactory(ITestOutputHelper output)
            {
                _output = output;
            }

            protected override ILogger NewInstance(string name, params LogLevel[] supportedLogLevels)
            {
                return new XunitLogger(_output, name, LogLevel.Debug, LogLevel.Info, LogLevel.Warning, LogLevel.Error);
            }
        }

        #endregion
    }
}
#endif