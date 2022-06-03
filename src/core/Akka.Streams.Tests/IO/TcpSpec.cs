//-----------------------------------------------------------------------
// <copyright file="TcpSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.IO;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;
using Tcp = Akka.Streams.Dsl.Tcp;
using static FluentAssertions.FluentActions;

namespace Akka.Streams.Tests.IO
{
    public class TcpSpec : TcpHelper
    {
        public TcpSpec(ITestOutputHelper helper) : base(@"
akka.loglevel = DEBUG
akka.stream.materializer.subscription-timeout.timeout = 2s", helper)
        {
        }

        [Fact]
        public async Task Outgoing_TCP_stream_must_work_in_the_happy_case()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var testData = ByteString.FromBytes(new byte[] {1, 2, 3, 4, 5});

                var server = await new Server(this).InitializeAsync();
                var tcpReadProbe = new TcpReadProbe(this);
                var tcpWriteProbe = new TcpWriteProbe(this);

                Source.FromPublisher(tcpWriteProbe.PublisherProbe)
                    .Via(Sys.TcpStream().OutgoingConnection(server.Address))
                    .To(Sink.FromSubscriber(tcpReadProbe.SubscriberProbe))
                    .Run(Materializer);
                var serverConnection = await server.WaitAcceptAsync();

                await ValidateServerClientCommunicationAsync(testData, serverConnection, tcpReadProbe,
                    tcpWriteProbe);
                
                await tcpWriteProbe.CloseAsync();
                await tcpReadProbe.CloseAsync();
                server.Close();
            }, Materializer);
        }

        [Fact]
        public async Task Outgoing_TCP_stream_must_be_able_to_write_a_sequence_of_ByteStrings()
        {
            var server = await new Server(this).InitializeAsync();
            var testInput = Enumerable.Range(0, 256).Select(i => ByteString.FromBytes(new[] {Convert.ToByte(i)}));
            var expectedOutput = ByteString.FromBytes(Enumerable.Range(0, 256).Select(Convert.ToByte).ToArray());

            Source.From(testInput)
                .Via(Sys.TcpStream().OutgoingConnection(server.Address))
                .To(Sink.Ignore<ByteString>())
                .Run(Materializer);

            var serverConnection = await server.WaitAcceptAsync();
            serverConnection.Read(256);
            (await serverConnection.WaitReadAsync()).Should().BeEquivalentTo(expectedOutput);
        }
        
        [Fact]
        public async Task Outgoing_TCP_stream_must_be_able_to_read_a_sequence_of_ByteStrings()
        {
            var server = await new Server(this).InitializeAsync();
            var testInput = new ByteString[255];
            var testOutput = new byte[255];
            for (byte i = 0; i < 255; i++)
            {
                testInput[i] = ByteString.FromBytes(new [] {i});
                testOutput[i] = i;
            }

            var expectedOutput = ByteString.FromBytes(testOutput);
            var idle = new TcpWriteProbe(this); //Just register an idle upstream

            var resultFuture =
                Source.FromPublisher(idle.PublisherProbe)
                    .Via(Sys.TcpStream().OutgoingConnection(server.Address))
                    .RunAggregate(ByteString.Empty, (acc, input) => acc + input, Materializer);
            var serverConnection = await server.WaitAcceptAsync();

            foreach (var input in testInput)
                serverConnection.Write(input);

            serverConnection.ConfirmedClose();
            var result = await resultFuture.ShouldCompleteWithin(3.Seconds());
            result.ShouldBe(expectedOutput);
        }

        [Fact]
        public async Task Outgoing_TCP_stream_must_fail_the_materialized_task_when_the_connection_fails()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var tcpWriteProbe = new TcpWriteProbe(this);
                var task =
                    Source.FromPublisher(tcpWriteProbe.PublisherProbe)
                        .ViaMaterialized(
                            Sys.TcpStream()
                                .OutgoingConnection(new DnsEndPoint("example.com", 666),
                                    connectionTimeout: TimeSpan.FromSeconds(1)), Keep.Right)
                        .ToMaterialized(Sink.Ignore<ByteString>(), Keep.Left)
                        .Run(Materializer);

                await Awaiting(() => task.ShouldCompleteWithin(3.Seconds()))
                    .Should().ThrowAsync<Exception>().WithMessage("Connection failed*");
            }, Materializer);
        }

        [Fact]
        public async Task Outgoing_TCP_stream_must_work_when_client_closes_write_then_remote_closes_write()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var testData = ByteString.FromBytes(new byte[] { 1, 2, 3, 4, 5 });
                var server = await new Server(this).InitializeAsync();

                var tcpWriteProbe = new TcpWriteProbe(this);
                var tcpReadProbe = new TcpReadProbe(this);

                Source.FromPublisher(tcpWriteProbe.PublisherProbe)
                    .Via(Sys.TcpStream().OutgoingConnection(server.Address))
                    .To(Sink.FromSubscriber(tcpReadProbe.SubscriberProbe))
                    .Run(Materializer);
                var serverConnection = await server.WaitAcceptAsync();

                // Client can still write
                await tcpWriteProbe.WriteAsync(testData);
                serverConnection.Read(5);
                (await serverConnection.WaitReadAsync()).Should().BeEquivalentTo(testData);

                // Close client side write
                await tcpWriteProbe.CloseAsync();
                await serverConnection.ExpectClosedAsync(Akka.IO.Tcp.PeerClosed.Instance);

                // Server can still write
                serverConnection.Write(testData);
                (await tcpReadProbe.ReadAsync(5)).Should().BeEquivalentTo(testData);

                // Close server side write
                serverConnection.ConfirmedClose();
                tcpReadProbe.SubscriberProbe.ExpectComplete();

                await serverConnection.ExpectClosedAsync(Akka.IO.Tcp.ConfirmedClosed.Instance);
                await serverConnection.ExpectTerminatedAsync();
            }, Materializer);
        }

        [Fact]
        public async Task Outgoing_TCP_stream_must_work_when_remote_closes_write_then_client_closes_write()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var testData = ByteString.FromBytes(new byte[] {1, 2, 3, 4, 5});
                var server = await new Server(this).InitializeAsync();

                var tcpWriteProbe = new TcpWriteProbe(this);
                var tcpReadProbe = new TcpReadProbe(this);
                Source.FromPublisher(tcpWriteProbe.PublisherProbe)
                    .Via(Sys.TcpStream().OutgoingConnection(server.Address))
                    .To(Sink.FromSubscriber(tcpReadProbe.SubscriberProbe))
                    .Run(Materializer);
                var serverConnection = await server.WaitAcceptAsync();

                // Server can still write
                serverConnection.Write(testData);
                (await tcpReadProbe.ReadAsync(5)).Should().BeEquivalentTo(testData);

                // Close server side write
                serverConnection.ConfirmedClose();
                tcpReadProbe.SubscriberProbe.ExpectComplete();

                // Client can still write
                await tcpWriteProbe.WriteAsync(testData);
                serverConnection.Read(5);
                (await serverConnection.WaitReadAsync()).Should().BeEquivalentTo(testData);

                // Close client side write
                await tcpWriteProbe.CloseAsync();
                await serverConnection.ExpectClosedAsync(Akka.IO.Tcp.ConfirmedClosed.Instance);
                await serverConnection.ExpectTerminatedAsync();
            }, Materializer);
        }

        [Fact(Skip = "FIXME: actually this is about half-open connection. No other .NET socket lib supports that")]
        public async Task Outgoing_TCP_stream_must_work_when_client_closes_read_then_client_closes_write()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var testData = ByteString.FromBytes(new byte[] { 1, 2, 3, 4, 5 });
                var server = await new Server(this).InitializeAsync();

                var tcpWriteProbe =new TcpWriteProbe(this);
                var tcpReadProbe = new TcpReadProbe(this);
                Source.FromPublisher(tcpWriteProbe.PublisherProbe)
                    .Via(Sys.TcpStream().OutgoingConnection(server.Address))
                    .To(Sink.FromSubscriber(tcpReadProbe.SubscriberProbe))
                    .Run(Materializer);
                var serverConnection = await server.WaitAcceptAsync();

                // Server can still write
                serverConnection.Write(testData);
                (await tcpReadProbe.ReadAsync(5)).Should().BeEquivalentTo(testData);

                // Close client side read
                (await tcpReadProbe.TcpReadSubscription()).Cancel();

                // Client can still write
                await tcpWriteProbe.WriteAsync(testData);
                serverConnection.Read(5);
                (await serverConnection.WaitReadAsync()).Should().BeEquivalentTo(testData);

                // Close client side write
                await tcpWriteProbe.CloseAsync();

                // Need a write on the server side to detect the close event
                await AwaitAssertAsync(async () =>
                {
                    serverConnection.Write(testData);
                    await serverConnection.ExpectClosedAsync(c => c.IsErrorClosed, TimeSpan.FromMilliseconds(500));
                }, TimeSpan.FromSeconds(5));

                await serverConnection.ExpectTerminatedAsync();
            }, Materializer);
        }

        [Fact]
        public async Task Outgoing_TCP_stream_must_work_when_client_closes_read_then_server_closes_write_then_client_closes_write()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var testData = ByteString.FromBytes(new byte[] { 1, 2, 3, 4, 5 });
                var server = await new Server(this).InitializeAsync();

                var tcpWriteProbe = new TcpWriteProbe(this);
                var tcpReadProbe = new TcpReadProbe(this);
                Source.FromPublisher(tcpWriteProbe.PublisherProbe)
                    .Via(Sys.TcpStream().OutgoingConnection(server.Address))
                    .To(Sink.FromSubscriber(tcpReadProbe.SubscriberProbe))
                    .Run(Materializer);
                var serverConnection = await server.WaitAcceptAsync();

                // Server can still write
                serverConnection.Write(testData);
                (await tcpReadProbe.ReadAsync(5)).Should().BeEquivalentTo(testData);

                // Close client side read
                (await tcpReadProbe.TcpReadSubscription()).Cancel();

                // Client can still write
                await tcpWriteProbe.WriteAsync(testData);
                serverConnection.Read(5);
                (await serverConnection.WaitReadAsync()).Should().BeEquivalentTo(testData);

                serverConnection.ConfirmedClose();

                // Close client side write
                await tcpWriteProbe.CloseAsync();
                await serverConnection.ExpectClosedAsync(Akka.IO.Tcp.ConfirmedClosed.Instance);
                await serverConnection.ExpectTerminatedAsync();
            }, Materializer);
        }

        [Fact]
        public async Task Outgoing_TCP_stream_must_shut_everything_down_if_client_signals_error()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var testData = ByteString.FromBytes(new byte[] { 1, 2, 3, 4, 5 });
                var server = await new Server(this).InitializeAsync();

                var tcpWriteProbe = new TcpWriteProbe(this);
                var tcpReadProbe = new TcpReadProbe(this);
                Source.FromPublisher(tcpWriteProbe.PublisherProbe)
                    .Via(Sys.TcpStream().OutgoingConnection(server.Address))
                    .To(Sink.FromSubscriber(tcpReadProbe.SubscriberProbe))
                    .Run(Materializer);
                var serverConnection = await server.WaitAcceptAsync();

                // Server can still write
                serverConnection.Write(testData);
                (await tcpReadProbe.ReadAsync(5)).Should().BeEquivalentTo(testData);
                
                // Client can still write
                await tcpWriteProbe.WriteAsync(testData);
                serverConnection.Read(5);
                (await serverConnection.WaitReadAsync()).Should().BeEquivalentTo(testData);

                // Cause error
                var subscription = await tcpWriteProbe.TcpWriteSubscription();
                subscription.SendError(new IllegalStateException("test"));

                await tcpReadProbe.SubscriberProbe.ExpectErrorAsync();
                await serverConnection.ExpectClosedAsync(c=>c.IsErrorClosed);
                await serverConnection.ExpectTerminatedAsync();
            }, Materializer);
        }

        [Fact]
        public async Task Outgoing_TCP_stream_must_shut_everything_down_if_client_signals_error_after_remote_has_closed_write()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var testData = ByteString.FromBytes(new byte[] { 1, 2, 3, 4, 5 });
                var server = await new Server(this).InitializeAsync();

                var tcpWriteProbe = new TcpWriteProbe(this);
                var tcpReadProbe = new TcpReadProbe(this);
                Source.FromPublisher(tcpWriteProbe.PublisherProbe)
                    .Via(Sys.TcpStream().OutgoingConnection(server.Address))
                    .To(Sink.FromSubscriber(tcpReadProbe.SubscriberProbe))
                    .Run(Materializer);
                var serverConnection = await server.WaitAcceptAsync();

                // Server can still write
                serverConnection.Write(testData);
                (await tcpReadProbe.ReadAsync(5)).Should().BeEquivalentTo(testData);

                // Close remote side write
                serverConnection.ConfirmedClose();
                tcpReadProbe.SubscriberProbe.ExpectComplete();

                // Client can still write
                await tcpWriteProbe.WriteAsync(testData);
                serverConnection.Read(5);
                (await serverConnection.WaitReadAsync()).Should().BeEquivalentTo(testData);

                var subscription = await tcpWriteProbe.TcpWriteSubscription();
                subscription.SendError(new IllegalStateException("test"));
                
                await serverConnection.ExpectClosedAsync(c => c.IsErrorClosed);
                await serverConnection.ExpectTerminatedAsync();
            }, Materializer);
        }

        [Fact]
        public async Task Outgoing_TCP_stream_must_shut_down_both_streams_when_connection_is_aborted_remotely()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                // Client gets a PeerClosed event and does not know that the write side is also closed
                var server = await new Server(this).InitializeAsync();

                var tcpWriteProbe = new TcpWriteProbe(this);
                var tcpReadProbe = new TcpReadProbe(this);

                Source.FromPublisher(tcpWriteProbe.PublisherProbe)
                    .Via(Sys.TcpStream().OutgoingConnection(server.Address))
                    .To(Sink.FromSubscriber(tcpReadProbe.SubscriberProbe))
                    .Run(Materializer);
                var serverConnection = await server.WaitAcceptAsync();

                serverConnection.Abort();
                await tcpReadProbe.SubscriberProbe.ExpectSubscriptionAndErrorAsync();
                var subscription = await tcpWriteProbe.TcpWriteSubscription();
                await subscription.ExpectCancellationAsync();

                await serverConnection.ExpectTerminatedAsync();
            }, Materializer);
        }

        [Fact]
        public async Task Outgoing_TCP_stream_must_materialize_correctly_when_used_in_multiple_flows()
        {
            var testData = ByteString.FromBytes(new byte[] {1, 2, 3, 4, 5});
            var server = await new Server(this).InitializeAsync();

            var tcpWriteProbe1 = new TcpWriteProbe(this);
            var tcpReadProbe1 = new TcpReadProbe(this);
            var tcpWriteProbe2 = new TcpWriteProbe(this);
            var tcpReadProbe2 = new TcpReadProbe(this);
            var outgoingConnection =
                new Tcp().CreateExtension(Sys as ExtendedActorSystem).OutgoingConnection(server.Address);

            var conn1F = Source.FromPublisher(tcpWriteProbe1.PublisherProbe)
                .ViaMaterialized(outgoingConnection, Keep.Both)
                .To(Sink.FromSubscriber(tcpReadProbe1.SubscriberProbe))
                .Run(Materializer).Item2;
            var serverConnection1 = await server.WaitAcceptAsync();
            var conn2F = Source.FromPublisher(tcpWriteProbe2.PublisherProbe)
                .ViaMaterialized(outgoingConnection, Keep.Both)
                .To(Sink.FromSubscriber(tcpReadProbe2.SubscriberProbe))
                .Run(Materializer).Item2;
            var serverConnection2 = await server.WaitAcceptAsync();

            await ValidateServerClientCommunicationAsync(testData, serverConnection1, tcpReadProbe1, tcpWriteProbe1);
            await ValidateServerClientCommunicationAsync(testData, serverConnection2, tcpReadProbe2, tcpWriteProbe2);
            
            var conn1 = await conn1F;
            var conn2 = await conn2F;

            // Since we have already communicated over the connections we can have short timeouts for the tasks
            ((IPEndPoint) conn1.RemoteAddress).Port.Should().Be(((IPEndPoint) server.Address).Port);
            ((IPEndPoint) conn2.RemoteAddress).Port.Should().Be(((IPEndPoint) server.Address).Port);
            ((IPEndPoint) conn1.LocalAddress).Port.Should().NotBe(((IPEndPoint) conn2.LocalAddress).Port);

            await tcpWriteProbe1.CloseAsync();
            await tcpReadProbe1.CloseAsync();

            server.Close();
        }

        [Fact]
        public async Task Outgoing_TCP_stream_must_properly_full_close_if_requested()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var serverAddress = TestUtils.TemporaryServerAddress();
                var writeButIgnoreRead = Flow.FromSinkAndSource(Sink.Ignore<ByteString>(),
                    Source.Single(ByteString.FromString("Early response")), Keep.Right);

                var task = 
                    Sys.TcpStream()
                        .Bind(serverAddress.Address.ToString(), serverAddress.Port, halfClose: false)
                        .ToMaterialized(
                            Sink.ForEach<Tcp.IncomingConnection>(conn => conn.Flow.Join(writeButIgnoreRead).Run(Materializer)),
                            Keep.Left)
                        .Run(Materializer);
                await task.ShouldCompleteWithin(3.Seconds());
                var binding = task.Result;

                var (promise, result) = Source.Maybe<ByteString>()
                    .Via(Sys.TcpStream().OutgoingConnection(serverAddress.Address.ToString(), serverAddress.Port))
                    .ToMaterialized(Sink.Aggregate<ByteString, ByteString>(ByteString.Empty, (s, s1) => s + s1), Keep.Both)
                    .Run(Materializer);

                await result.ShouldCompleteWithin(5.Seconds());
                result.Result.Should().BeEquivalentTo(ByteString.FromString("Early response"));

                promise.SetResult(null); // close client upstream, no more data
                await binding.Unbind().ShouldCompleteWithin(3.Seconds());

            }, Materializer);
        }

        [Fact]
        public async Task Outgoing_TCP_stream_must_Echo_should_work_even_if_server_is_in_full_close_mode()
        {
            var serverAddress = TestUtils.TemporaryServerAddress();

            var binding = await Sys.TcpStream()
                    .Bind(serverAddress.Address.ToString(), serverAddress.Port)
                    .ToMaterialized(
                        Sink.ForEach<Tcp.IncomingConnection>(conn => conn.Flow.Join(Flow.Create<ByteString>()).Run(Materializer)),
                        Keep.Left)
                    .Run(Materializer);

            var result = await Source.From(Enumerable.Repeat(0, 1000)
                .Select(i => ByteString.FromBytes(new[] {Convert.ToByte(i)})))
                .Via(Sys.TcpStream().OutgoingConnection(serverAddress, halfClose: true))
                .RunAggregate(0, (i, s) => i + s.Count, Materializer).ShouldCompleteWithin(10.Seconds());
            
            result.Should().Be(1000);

            await binding.Unbind();
        }

        [Fact]
        public async Task Outgoing_TCP_stream_must_handle_when_connection_actor_terminates_unexpectedly()
        {
            var system2 = ActorSystem.Create("system2", Sys.Settings.Config);
            try
            {
                InitializeLogger(system2);
                var mat2 = ActorMaterializer.Create(system2);

                var serverAddress = TestUtils.TemporaryServerAddress();
                var binding = system2.TcpStream()
                    .BindAndHandle(Flow.Create<ByteString>(), mat2, serverAddress.Address.ToString(), serverAddress.Port);

                var result = Source.Maybe<ByteString>()
                    .Via(system2.TcpStream().OutgoingConnection(serverAddress))
                    .RunAggregate(0, (i, s) => i + s.Count, mat2);

                // give some time for all TCP stream actor parties to actually 
                // get initialized, otherwise Kill command may run into the void
                await Task.Delay(500);

                // Getting rid of existing connection actors by using a blunt instrument
                system2.ActorSelection(system2.Tcp().Path / "$a" / "*").Tell(Kill.Instance);

                await Awaiting(() => result.ShouldCompleteWithin(3.Seconds()))
                    .Should().ThrowAsync<StreamTcpException>();

                await binding.Result.Unbind().ShouldCompleteWithin(3.Seconds());
            }
            finally
            {
                await ShutdownAsync(system2);
            }
        }

        [Fact]
        public async Task Outgoing_TCP_stream_must_not_thrown_on_unbind_after_system_has_been_shut_down()
        {
            var sys2 = ActorSystem.Create("shutdown-test-system", Sys.Settings.Config);

            try
            {
                var mat2 = sys2.Materializer();
                var address = TestUtils.TemporaryServerAddress();
                var binding = await sys2.TcpStream()
                    .BindAndHandle(Flow.Create<ByteString>(), mat2, address.Address.ToString(), address.Port);

                // Ensure server is running
                // and is possible to communicate with 
                await Source.Single(ByteString.FromString(""))
                    .Via(sys2.TcpStream().OutgoingConnection(address))
                    .RunWith(Sink.Ignore<ByteString>(), mat2).ShouldCompleteWithin(10.Seconds());

                await sys2.Terminate().ShouldCompleteWithin(10.Seconds());
                await binding.Unbind().ShouldCompleteWithin(10.Seconds());
            }
            finally
            {
                await ShutdownAsync(sys2);
            }
        }

        private async Task ValidateServerClientCommunicationAsync(ByteString testData, ServerConnection serverConnection, TcpReadProbe readProbe, TcpWriteProbe writeProbe)
        {
            serverConnection.Write(testData);
            serverConnection.Read(5);
            (await readProbe.ReadAsync(5)).Should().BeEquivalentTo(testData);
            await writeProbe.WriteAsync(testData);
            (await serverConnection.WaitReadAsync()).Should().BeEquivalentTo(testData);
        }
        
        private Sink<Tcp.IncomingConnection, Task<Done>> EchoHandler() =>
            Sink.ForEach<Tcp.IncomingConnection>(c => c.Flow.Join(Flow.Create<ByteString>()).Run(Materializer));

        [Fact]
        public async Task Tcp_listen_stream_must_be_able_to_implement_echo()
        {
            var serverAddress = TestUtils.TemporaryServerAddress();
            var (bindTask, echoServerFinish) = Sys.TcpStream()
                .Bind(serverAddress.Address.ToString(), serverAddress.Port)
                .ToMaterialized(EchoHandler(), Keep.Both)
                .Run(Materializer);

            // make sure that the server has bound to the socket
            var binding = await bindTask.ShouldCompleteWithin(3.Seconds());

            var testInput = Enumerable.Range(0, 255)
                .Select(i => ByteString.FromBytes(new[] {Convert.ToByte(i)}))
                .ToList();

            var expectedOutput = testInput.Aggregate(ByteString.Empty, (agg, b) => agg.Concat(b));

            var result = await Source.From(testInput)
                    .Via(Sys.TcpStream().OutgoingConnection(serverAddress))
                    .RunAggregate(ByteString.Empty, (agg, b) => agg.Concat(b), Materializer)
                    .ShouldCompleteWithin(10.Seconds());
            
            result.Should().BeEquivalentTo(expectedOutput);
            await binding.Unbind().ShouldCompleteWithin(3.Seconds());
            await echoServerFinish.ShouldCompleteWithin(3.Seconds());
        }

        [Fact]
        public async Task Tcp_listen_stream_must_work_with_a_chain_of_echoes()
        {
            var serverAddress = TestUtils.TemporaryServerAddress();
            var (bindTask, echoServerFinish) = Sys.TcpStream()
                .Bind(serverAddress.Address.ToString(), serverAddress.Port)
                .ToMaterialized(EchoHandler(), Keep.Both)
                .Run(Materializer);

            // make sure that the server has bound to the socket
            var binding = await bindTask.ShouldCompleteWithin(3.Seconds());

            var echoConnection = Sys.TcpStream().OutgoingConnection(serverAddress);

            var testInput = Enumerable.Range(0, 255)
                .Select(i => ByteString.FromBytes(new[] { Convert.ToByte(i) }))
                .ToList();

            var expectedOutput = testInput.Aggregate(ByteString.Empty, (agg, b) => agg.Concat(b));

            var result = await Source.From(testInput)
                .Via(echoConnection) // The echoConnection is reusable
                .Via(echoConnection)
                .Via(echoConnection)
                .Via(echoConnection)
                .RunAggregate(ByteString.Empty, (agg, b) => agg.Concat(b), Materializer)
                .ShouldCompleteWithin(3.Seconds());
            
            result.Should().BeEquivalentTo(expectedOutput);
            await binding.Unbind().ShouldCompleteWithin(3.Seconds());
            await echoServerFinish.ShouldCompleteWithin(3.Seconds());
        }

        [Fact]
        public async Task Tcp_stream_must_be_able_to_be_unbound_multiple_times()
        {
            var serverAddress = TestUtils.TemporaryServerAddress();
            var (bindTask, echoServerFinish) = Sys.TcpStream()
                .Bind(serverAddress.Address.ToString(), serverAddress.Port)
                .ToMaterialized(EchoHandler(), Keep.Both)
                .Run(Materializer);

            // make sure that the server has bound to the socket
            var binding = await bindTask.ShouldCompleteWithin(3.Seconds());

            await Task.WhenAll(
                binding.Unbind(),
                binding.Unbind(),
                binding.Unbind(),
                binding.Unbind(),
                binding.Unbind(),
                binding.Unbind(),
                binding.Unbind())
                .ShouldCompleteWithin(3.Seconds());
            
            await echoServerFinish.ShouldCompleteWithin(3.Seconds());
        }

        [Fact]
        public async Task Tcp_listen_stream_must_bind_and_unbind_correctly()
        {
            // in JVM we filter for BindException, on .NET it's SocketException
            await EventFilter.Exception<SocketException>().ExpectAsync(2, async () =>
            {
                var address = TestUtils.TemporaryServerAddress();
                var probe1 = this.CreateSubscriberProbe<Tcp.IncomingConnection>();
                var bind = Sys.TcpStream().Bind(address.Address.ToString(), address.Port);

                // bind succeed, we have local address
                var binding1 = await bind.To(Sink.FromSubscriber(probe1)).Run(Materializer)
                    .ShouldCompleteWithin(3.Seconds());

                await probe1.ExpectSubscriptionAsync();

                var probe2 = this.CreateManualSubscriberProbe<Tcp.IncomingConnection>();
                var binding2F = bind.To(Sink.FromSubscriber(probe2)).Run(Materializer);
                (await probe2.ExpectSubscriptionAndErrorAsync()).Should().BeOfType<BindFailedException>();

                var probe3 = this.CreateManualSubscriberProbe<Tcp.IncomingConnection>();
                var binding3F = bind.To(Sink.FromSubscriber(probe3)).Run(Materializer);
                (await probe3.ExpectSubscriptionAndErrorAsync()).Should().BeOfType<BindFailedException>();

                await Awaiting(() => binding2F.ShouldCompleteWithin(3.Seconds()))
                    .Should().ThrowAsync<BindFailedException>();
                await Awaiting(() => binding3F.ShouldCompleteWithin(3.Seconds()))
                    .Should().ThrowAsync<BindFailedException>();
                
                // Now unbind first
                await binding1.Unbind().ShouldCompleteWithin(3.Seconds());
                probe1.ExpectComplete();

                var probe4 = this.CreateManualSubscriberProbe<Tcp.IncomingConnection>();
                // bind succeeded, we have local address
                var binding4 = await bind.To(Sink.FromSubscriber(probe4)).Run(Materializer).ShouldCompleteWithin(3.Seconds());
                await probe4.ExpectSubscriptionAsync();

                // clean up
                await binding4.Unbind().ShouldCompleteWithin(5.Seconds());
            });
        }

        [Fact(Skip = "FIXME: unexpected ErrorClosed")]
        public async Task Tcp_listen_stream_must_not_shut_down_connections_after_the_connection_stream_cancelled()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var thousandByteStrings = Enumerable.Range(0, 1000)
                    .Select(_ => ByteString.FromBytes(new byte[] { 0 }))
                    .ToArray();

                var serverAddress = TestUtils.TemporaryServerAddress();
                var (bindingTask, completeTask) = Sys.TcpStream()
                    .Bind(serverAddress.Address.ToString(), serverAddress.Port)
                    .Take(1)
                    .ToMaterialized(Sink.ForEachAsync<Tcp.IncomingConnection>(1, async tcp =>
                    {
                        await Task.Delay(1000); // we're testing here to see if it survives such race
                        tcp.Flow.Join(Flow.Create<ByteString>()).Run(Materializer);
                    }), Keep.Both)
                    .Run(Materializer);

                // make sure server is running first
                await bindingTask.ShouldCompleteWithin(3.Seconds());
                var result = bindingTask.Result;

                // then connect, should trigger a block and then
                var total = Source.From(thousandByteStrings)
                    .Via(Sys.TcpStream().OutgoingConnection(serverAddress))
                    .RunAggregate(0, (i, s) => i + s.Count, Materializer);

                (await total.ShouldCompleteWithin(5.Seconds())).Should().Be(1000);
            }, Materializer);
        }

        [Fact(Skip="FIXME StreamTcpException")]
        public async Task Tcp_listen_stream_must_shut_down_properly_even_if_some_accepted_connection_Flows_have_not_been_subscribed_to ()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var serverAddress = TestUtils.TemporaryServerAddress();
                var firstClientConnected = new TaskCompletionSource<NotUsed>();
                var takeTwoAndDropSecond = Flow.Create<Tcp.IncomingConnection>().Select(c =>
                {
                    firstClientConnected.TrySetResult(NotUsed.Instance);
                    return c;
                }).Grouped(2).Take(1).Select(e => e.First());

                var task = Sys.TcpStream()
                    .Bind(serverAddress.Address.ToString(), serverAddress.Port)
                    .Via(takeTwoAndDropSecond)
                    .RunForeach(c => c.Flow.Join(Flow.Create<ByteString>()).Run(Materializer), Materializer);

                var folder = Source.From(Enumerable.Range(0, 100).Select(_ => ByteString.FromBytes(new byte[] {0})))
                    .Via(Sys.TcpStream().OutgoingConnection(serverAddress))
                    .Aggregate(0, (i, s) => i + s.Count)
                    .ToMaterialized(Sink.First<int>(), Keep.Right);

                var total = folder.Run(Materializer);

                await firstClientConnected.Task.ShouldCompleteWithin(2.Seconds());
                var rejected = folder.Run(Materializer);

                (await total.ShouldCompleteWithin(10.Seconds())).Should().Be(100);
                
                await rejected.ShouldThrowWithin<StreamTcpException>(3.Seconds());
            }, Materializer);
        }
    }
}
