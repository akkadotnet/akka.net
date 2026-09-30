//-----------------------------------------------------------------------
// <copyright file="UdpDisabledBufferPoolSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using Akka.IO;
using Akka.IO.Buffers;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.IO
{
    /// <summary>
    /// UDP always uses <see cref="DisabledBufferPool"/>; its <c>buffer-size</c> key stays configurable.
    /// </summary>
    public class UdpDisabledBufferPoolSpec : AkkaSpec
    {
        public UdpDisabledBufferPoolSpec(ITestOutputHelper output)
            : base(@"
                    akka.io.udp.max-channels = unlimited
                    akka.io.udp.nr-of-selectors = 1
                    akka.io.udp.disabled-buffer-pool.buffer-size = 128", output)
        {
        }

        [Fact(DisplayName = "UDP should rent buffers sized from the customized disabled-buffer-pool.buffer-size")]
        public void Should_use_the_customized_disabled_buffer_pool_size()
        {
            var udp = Udp.Instance.Apply(Sys);
            udp.SocketEventArgsPool.BufferPoolInfo.Type.Should().Be(typeof(DisabledBufferPool));

            var e = udp.SocketEventArgsPool.Acquire(TestActor);
            try
            {
                e.Count.Should().Be(128);
            }
            finally
            {
                udp.SocketEventArgsPool.Release(e);
            }
        }
    }
}
