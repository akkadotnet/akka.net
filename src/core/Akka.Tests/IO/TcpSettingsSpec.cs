//-----------------------------------------------------------------------
// <copyright file="TcpSettingsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.IO;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.IO
{
    public class TcpSettingsSpec
    {
        [Fact]
        public void TcpSettings_should_parse_all_akka_io_tcp_config_values_correctly()
        {
            // Arrange: load the default reference config
            var config = ConfigurationFactory.Default();
            var tcpConfig = config.GetConfig("akka.io.tcp");
            var settings = TcpSettings.Create(tcpConfig);

            // Assert: all values match akka.conf reference
            settings.TraceLogging.Should().BeFalse();
            settings.BatchAcceptLimit.Should().Be(Environment.ProcessorCount * 2);
            settings.RegisterTimeout.Should().Be(TimeSpan.FromSeconds(5));
            settings.ManagementDispatcher.Should().Be("akka.actor.internal-dispatcher");
            settings.FinishConnectRetries.Should().Be(5);
            settings.OutgoingSocketForceIpv4.Should().BeFalse();
            settings.WriteCommandsQueueMaxSize.Should().Be(-1);
            settings.SendBufferSize.Should().Be(8192);
            settings.ReceiveBufferSize.Should().Be(8192);
            settings.MaxFrameSizeBytes.Should().Be(4096);
        }
    }
} 