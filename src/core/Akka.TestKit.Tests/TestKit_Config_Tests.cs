//-----------------------------------------------------------------------
// <copyright file="TestKit_Config_Tests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Reflection;
using Akka.Configuration;
using Akka.Dispatch;
using Xunit;

namespace Akka.TestKit.Tests.Xunit2
{
    // ReSharper disable once InconsistentNaming
    public class TestKit_Config_Tests : TestKit.Xunit.TestKit
    {
        [Fact]
        public void DefaultValues_should_be_correct()
        {
            TestKitSettings.DefaultTimeout.ShouldBe(TimeSpan.FromSeconds(5));
            TestKitSettings.SingleExpectDefault.ShouldBe(TimeSpan.FromSeconds(3));
            TestKitSettings.ExpectNoMessageDefault.ShouldBe(TimeSpan.FromSeconds(3));
            TestKitSettings.TestEventFilterLeeway.ShouldBe(TimeSpan.FromSeconds(3));
            TestKitSettings.TestTimeFactor.ShouldBe(1);
            TestKitSettings.TestKitStartupTimeout.ShouldBe(TimeSpan.FromSeconds(5));
            var callingThreadDispatcherTypeName = typeof(CallingThreadDispatcherConfigurator).FullName + ", " + typeof(CallingThreadDispatcher).Assembly.GetName().Name;
            Assert.False(Sys.Settings.Config.IsEmpty);
            Sys.Settings.Config.GetString("akka.test.calling-thread-dispatcher.type", null).ShouldBe(callingThreadDispatcherTypeName);
            Sys.Settings.Config.GetString("akka.test.test-actor.dispatcher.type", null).ShouldBe(callingThreadDispatcherTypeName);
            CallingThreadDispatcher.Id.ShouldBe("akka.test.calling-thread-dispatcher");
        }
    }
    
    public class TestKitCustomConfigTests : TestKit.Xunit.TestKit
    {
        private static readonly Config Config = """
        akka.test {
            timefactor =  2.0
            filter-leeway = 4s
            single-expect-default = 5s
            expect-no-message-default = 6s
            default-timeout = 7s
            startup-timeout = 8s
            calling-thread-dispatcher {
              type = "Akka.Dispatch.DispatcherConfigurator, Akka"
              throughput = 2147483647
            }
            
            test-actor.dispatcher {
              type = "Akka.Dispatch.DispatcherConfigurator, Akka"
              throughput = 2147483647
            }
        }
        """;
        
        public TestKitCustomConfigTests(): base(Config)
        {
        }
        
        [Fact]
        public void DefaultValues_should_be_correct()
        {
            TestKitSettings.TestTimeFactor.ShouldBe(2);
            TestKitSettings.TestEventFilterLeeway.ShouldBe(TimeSpan.FromSeconds(4));
            TestKitSettings.SingleExpectDefault.ShouldBe(TimeSpan.FromSeconds(5));
            TestKitSettings.ExpectNoMessageDefault.ShouldBe(TimeSpan.FromSeconds(6));
            TestKitSettings.DefaultTimeout.ShouldBe(TimeSpan.FromSeconds(7));
            TestKitSettings.TestKitStartupTimeout.ShouldBe(TimeSpan.FromSeconds(8));
            
            var callingThreadDispatcherTypeName = typeof(DispatcherConfigurator).FullName + ", " + typeof(DispatcherConfigurator).Assembly.GetName().Name;
            Assert.False(Sys.Settings.Config.IsEmpty);
            
            Sys.Settings.Config.GetString("akka.test.calling-thread-dispatcher.type", null).ShouldBe(callingThreadDispatcherTypeName);
            Sys.Settings.Config.GetString("akka.test.test-actor.dispatcher.type", null).ShouldBe(callingThreadDispatcherTypeName);
            CallingThreadDispatcher.Id.ShouldBe("akka.test.calling-thread-dispatcher");
        }
    }

    // ReSharper disable once InconsistentNaming
    public class TestKitDefaultConfig_RemoteBatchingOverride_Tests
    {
        [Fact]
        public void DotNetty_batching_override_should_resolve_under_akka_remote()
        {
            // The TestKit's Reference.conf disables DotNetty write batching to avoid
            // flakiness in low-frequency Akka.Remote tests. Akka.Remote only reads this
            // setting at akka.remote.dot-netty.tcp.batching.enabled -- DotNettyTransportSettings
            // resolves it via config.GetConfig("batching") relative to akka.remote.dot-netty.tcp
            // -- so the override has to live at that exact path or it silently never applies.
            var config = TestKitBase.DefaultConfig;

            config.HasPath("akka.remote.dot-netty.tcp.batching.enabled").ShouldBeTrue();
            config.GetBoolean("akka.remote.dot-netty.tcp.batching.enabled").ShouldBeFalse();
        }
    }
}

