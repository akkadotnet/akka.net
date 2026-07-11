//-----------------------------------------------------------------------
// <copyright file="PipeBufferSizeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Configuration;
using Akka.IO;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.IO
{
    /// <summary>
    /// Covers <see cref="Inet.SO.PipeBufferSize"/> -- the socket option that lets a caller (e.g.
    /// Artery) raise the pause/resume watermarks of a <c>TcpConnection</c>'s internal input
    /// <see cref="System.IO.Pipelines.Pipe"/> above Akka.IO's default (derived from
    /// <c>akka.io.tcp.receive-buffer-size</c>), without changing that default for every other
    /// Akka.IO TCP connection -- and <see cref="TcpConnection.ResolvePipeBufferSize"/>, the pure
    /// function <c>TcpIncomingConnection</c>/<c>TcpOutgoingConnection</c> use to pick between the
    /// option and the fallback when constructing their input pipe's <c>PipeOptions</c>.
    /// </summary>
    public class PipeBufferSizeSpec
    {
        private static TcpSettings DefaultTcpSettings =>
            TcpSettings.Create(ConfigurationFactory.Default().GetConfig("akka.io.tcp"));

        [Fact(DisplayName = "PipeBufferSize should expose the configured Size")]
        public void PipeBufferSize_should_expose_the_configured_size()
        {
            var option = new Inet.SO.PipeBufferSize(1024 * 1024);
            option.Size.Should().Be(1024 * 1024);
        }

        [Theory(DisplayName = "PipeBufferSize should reject a non-positive size")]
        [InlineData(0)]
        [InlineData(-1)]
        public void PipeBufferSize_should_reject_a_non_positive_size(int size)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Inet.SO.PipeBufferSize(size));
        }

        [Fact(DisplayName = "ResolvePipeBufferSize should fall back to TcpSettings.ReceiveBufferSize when no PipeBufferSize option is present")]
        public void ResolvePipeBufferSize_should_fall_back_to_ReceiveBufferSize_when_no_option_present()
        {
            var settings = DefaultTcpSettings;

            var resolved = TcpConnection.ResolvePipeBufferSize(settings, Array.Empty<Inet.SocketOption>());

            resolved.Should().Be(settings.ReceiveBufferSize);
        }

        [Fact(DisplayName = "ResolvePipeBufferSize should use the PipeBufferSize option's Size when present")]
        public void ResolvePipeBufferSize_should_use_the_option_when_present()
        {
            var settings = DefaultTcpSettings;
            var options = new Inet.SocketOption[] { new Inet.SO.PipeBufferSize(1024 * 1024) };

            var resolved = TcpConnection.ResolvePipeBufferSize(settings, options);

            resolved.Should().Be(1024 * 1024);
            resolved.Should().NotBe(settings.ReceiveBufferSize, "the option must override Akka.IO's default receive-buffer-size-derived watermark");
        }

        [Fact(DisplayName = "ResolvePipeBufferSize should ignore unrelated socket options")]
        public void ResolvePipeBufferSize_should_ignore_unrelated_options()
        {
            var settings = DefaultTcpSettings;
            var options = new Inet.SocketOption[] { new Inet.SO.ReceiveBufferSize(4096), new Inet.SO.ReuseAddress(true) };

            var resolved = TcpConnection.ResolvePipeBufferSize(settings, options);

            resolved.Should().Be(settings.ReceiveBufferSize);
        }

        [Fact(DisplayName = "ResolvePipeBufferSize should use the LAST PipeBufferSize option when more than one is present")]
        public void ResolvePipeBufferSize_should_use_the_last_option_when_more_than_one_present()
        {
            var settings = DefaultTcpSettings;
            var options = new List<Inet.SocketOption>
            {
                new Inet.SO.PipeBufferSize(64 * 1024),
                new Inet.SO.PipeBufferSize(1024 * 1024)
            };

            var resolved = TcpConnection.ResolvePipeBufferSize(settings, options);

            resolved.Should().Be(1024 * 1024);
        }
    }
}
