//-----------------------------------------------------------------------
// <copyright file="TcpHelperCloseObserverSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.IO;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.IO
{
    public sealed class TcpHelperCloseObserverSpec : TcpHelper
    {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(3);

        public TcpHelperCloseObserverSpec(ITestOutputHelper output) : base("akka.loglevel = DEBUG", output)
        {
        }

        [Fact(DisplayName = "Should_Route_ConfirmedClosed_To_The_Close_Requester_After_A_Read_Result")]
        public async Task Should_route_confirmed_closed_to_the_close_requester_after_a_read_result()
        {
            var connection = CreateTestProbe();
            var closeRequester = CreateTestProbe();
            var readRequester = CreateTestProbe();
            var watcher = CreateTestProbe();
            var client = Sys.ActorOf(TestClientProps(connection.Ref));
            await watcher.WatchAsync(client);

            await connection.ExpectMsgAsync<Tcp.Register>(TestTimeout);

            client.Tell(new ClientClose(Tcp.ConfirmedClose.Instance, closeRequester.Ref));
            await connection.ExpectMsgAsync<Tcp.ConfirmedClose>(TestTimeout);
            await connection.ExpectMsgAsync<Tcp.ResumeReading>(TestTimeout);

            client.Tell(new ClientRead(1, readRequester.Ref));
            await connection.ExpectMsgAsync<Tcp.ResumeReading>(TestTimeout);
            client.Tell(new Tcp.Received(new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes("x"))));
            var read = await readRequester.ExpectMsgAsync<ReadResult>(TestTimeout);
            read.Bytes.ToArray().Should().Equal(Encoding.UTF8.GetBytes("x"));

            client.Tell(Tcp.ConfirmedClosed.Instance);
            await closeRequester.ExpectMsgAsync<Tcp.ConfirmedClosed>(TestTimeout);
            await watcher.ExpectTerminatedAsync(client, TestTimeout);
        }
    }
}
