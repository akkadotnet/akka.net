//-----------------------------------------------------------------------
// <copyright file="DotNettyConfigurationLoggingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote.Transport.DotNetty;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;
using FluentAssertions;

namespace Akka.Remote.Tests.Transport
{
    /// <summary>
    /// Verify that the DotNetty configuration logging functionality works correctly.
    /// </summary>
    public class DotNettyConfigurationLoggingSpec : AkkaSpec
    {
        private static readonly Config ConfigWithLogging = ConfigurationFactory.ParseString(@"
            akka {
                loglevel = INFO
                actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""
                remote {
                    dot-netty.tcp {
                        port = 0
                        hostname = ""127.0.0.1""
                        log-dot-netty-config = true
                        batching {
                            enabled = true
                            max-pending-writes = 30
                            max-pending-bytes = 16k
                            flush-interval = 40ms
                        }
                    }
                }
            }");

        public DotNettyConfigurationLoggingSpec(ITestOutputHelper output) : base(ConfigWithLogging, output)
        {
        }

        [Fact]
        public void DotNettyTransportSettings_should_read_log_dot_netty_config_setting()
        {
            // Test that the setting is correctly read from configuration
            var config = Sys.Settings.Config.GetConfig("akka.remote.dot-netty.tcp");
            var settings = DotNettyTransportSettings.Create(config);
            settings.LogDotNettyConfig.Should().BeTrue();
        }

        [Fact]
        public void DotNettyTransport_should_log_configuration_when_enabled()
        {
            // Use EventFilter to capture the DotNetty configuration dump
            EventFilter.Info(start: "=== DotNetty Configuration Dump ===").ExpectOne(() =>
            {
                // Trigger DotNetty initialization by accessing the remote provider
                var extendedSystem = (ExtendedActorSystem)Sys;
                var remoteProvider = (RemoteActorRefProvider)extendedSystem.Provider;
                var defaultAddress = remoteProvider.DefaultAddress;
                
                // Verify we get a valid address
                defaultAddress.Should().NotBeNull();
                defaultAddress.Port.Should().BeGreaterThan(0);
            });
        }

        [Fact]
        public void DotNettyTransport_should_log_recycler_configuration()
        {
            // Use EventFilter to capture the ThreadLocalPool recycler configuration
            EventFilter.Info(contains: "ThreadLocalPool Recycler Configuration").ExpectOne(() =>
            {
                // Trigger DotNetty initialization
                var extendedSystem = (ExtendedActorSystem)Sys;
                var remoteProvider = (RemoteActorRefProvider)extendedSystem.Provider;
                var defaultAddress = remoteProvider.DefaultAddress;
                
                defaultAddress.Should().NotBeNull();
            });
        }

        [Fact]
        public void DotNettyTransport_with_recycler_disabled_should_log_disabled_state()
        {
            // Set environment variable to disable recycler (ARM64 fix)
            Environment.SetEnvironmentVariable("io.netty.recycler.maxCapacityPerThread", "0");
            
            try
            {
                // Use EventFilter to capture the disabled recycler message
                EventFilter.Info(contains: "Effective recycler state: DISABLED").ExpectOne(() =>
                {
                    // Trigger DotNetty initialization
                    var extendedSystem = (ExtendedActorSystem)Sys;
                    var remoteProvider = (RemoteActorRefProvider)extendedSystem.Provider;
                    var defaultAddress = remoteProvider.DefaultAddress;
                    
                    defaultAddress.Should().NotBeNull();
                });
            }
            finally
            {
                // Clean up environment variable
                Environment.SetEnvironmentVariable("io.netty.recycler.maxCapacityPerThread", null);
            }
        }
    }
} 