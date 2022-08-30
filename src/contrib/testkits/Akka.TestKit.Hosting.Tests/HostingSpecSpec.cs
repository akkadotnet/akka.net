// -----------------------------------------------------------------------
//  <copyright file="HostingSpecSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Event;
using Akka.Hosting;
using Akka.TestKit.TestActors;
using Xunit;
using Xunit.Abstractions;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Akka.TestKit.Hosting.Tests
{
    public class HostingSpecSpec: HostingSpec
    {
        private enum Echo
        { }

        public HostingSpecSpec(ITestOutputHelper output)
            : base(nameof(HostingSpecSpec), output, logLevel: LogLevel.Debug)
        {
        }

        protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
        {
            builder.WithActors((system, registry) =>
            {
                var echo = system.ActorOf(Props.Create(() => new SimpleEchoActor()));
                registry.Register<Echo>(echo);
            });
        }

        [Fact]
        public void ActorTest()
        {
            var echo = ActorRegistry.Get<Echo>();
            var probe = CreateTestProbe();
            
            echo.Tell("TestMessage", probe);
            var msg = probe.ExpectMsg("TestMessage");
            Log.Info(msg);
        }
    }
}