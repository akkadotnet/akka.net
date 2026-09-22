//-----------------------------------------------------------------------
// <copyright file="TcpUnbindSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.TestKit;
using Xunit;
using Tcp = Akka.Streams.Dsl.Tcp;

namespace Akka.Streams.Tests.IO
{
    /// <summary>
    /// Regression coverage for the <c>ConnectionSourceStage</c> unbind idle fast path
    /// (see <c>Akka.Streams.Implementation.IO.TcpStages</c>). When a server binding is
    /// unbound without ever having accepted a connection, <c>ServerBinding.Unbind()</c>
    /// must complete right away instead of waiting out the full
    /// <c>akka.stream.materializer.subscription-timeout.timeout</c> (5s by default).
    /// </summary>
    public class TcpUnbindSpec : TcpHelper
    {
        // Intentionally leave `akka.stream.materializer.subscription-timeout.timeout` at its
        // production default (5s) so this test exercises the real-world timing exposure.
        public TcpUnbindSpec(ITestOutputHelper helper) : base("", helper)
        {
        }

        [Fact(DisplayName = "Unbind should complete promptly when no connection is awaiting initialization")]
        public async Task Unbind_should_complete_promptly_when_no_connection_is_awaiting_initialization()
        {
            var binding = await Sys.TcpStream()
                .Bind("127.0.0.1", 0)
                .To(Sink.Ignore<Tcp.IncomingConnection>())
                .Run(Materializer)
                .WaitAsync(TimeSpan.FromSeconds(3));

            // No client ever connects, so _connectionFlowsAwaitingInitialization stays at its
            // resting value. The idle fast path in UnbindCompleted() should complete the stage
            // immediately rather than falling through to the BindShutdownTimer, which would take
            // the full subscription-timeout (5s by default) to fire.
            await binding.Unbind().WaitAsync(TimeSpan.FromSeconds(1));
        }
    }
}
