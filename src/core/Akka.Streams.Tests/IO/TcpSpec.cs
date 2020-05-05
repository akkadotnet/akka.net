//-----------------------------------------------------------------------
// <copyright file="TcpSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.IO;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using Tcp = Akka.Streams.Dsl.Tcp;

namespace Akka.Streams.Tests.IO
{
    public class TcpSpec : TcpHelper
    {
        public TcpSpec(ITestOutputHelper helper) : base(@"
akka.stream.materializer.subscription-timeout.timeout = 2s", helper)
        {
        }

        [Fact]
        public void Outgoing_TCP_stream_must_work_in_the_happy_case()
        {
            this.AssertAllStagesStopped(() =>
            {
                var testData = ByteString.FromBytes(new byte[] {1, 2, 3, 4, 5});

                var server = new Server(this);

                var tcpReadProbe = new TcpReadProbe(this);
                var tcpWriteProbe = new TcpWriteProbe(this);
                Source.FromPublisher(tcpWriteProbe.PublisherProbe)
                    .Via(Sys.TcpStream().OutgoingConnection(server.Address))
                    .To(Sink.FromSubscriber(tcpReadProbe.SubscriberProbe))
                    .Run(Materializer);
                var serverConnection = server.WaitAccept();

                ValidateServerClientCommunication(testData, serverConnection, tcpReadProbe, tcpWriteProbe);

                tcpWriteProbe.Close();
                tcpReadProbe.Close();
                server.Close();
            }, Materializer);
        }

        [Fact]
        public void Outgoing_TCP_stream_must_be_able_to_write_a_sequence_of_ByteStrings()
        {
            var server = new Server(this);
            var testInput = Enumerable.Range(0, 256).Select(i => ByteString.FromBytes(new[] {Convert.ToByte(i)}));
            var expectedOutput = ByteString.FromBytes(Enumerable.Range(0, 256).Select(Convert.ToByte).ToArray());

            Source.From(testInput)
                .Via(Sys.TcpStream().OutgoingConnection(server.Address))
                .To(Sink.Ignore<ByteString>())
                .Run(Materializer);

            var serverConnection = server.WaitAccept();
            serverConnection.Read(256);
            serverConnection.WaitRead().ShouldBeEquivalentTo(expectedOutput);
        }
        
        [Fact]
        public async Task Outgoing_TCP_stream_must_be_able_to_read_a_sequence_of_ByteStrings()
        {
            var server = new Server(this);
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
            var serverConnection = server.WaitAccept();

            foreach (var input in testInput)
                serverConnection.Write(input);

            serverConnection.ConfirmedClose();
            var result = await resultFuture;
            result.ShouldBe(expectedOutput);
        }

        [Fact(Skip="FIXME .net core / linux")]
        public void Outgoing_TCP_stream_must_fail_the_materialized_task_when_the_connection_fails()
        {
            this.AssertAllStagesStopped(() =>
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

                task.Invoking(t => t.Wait(TimeSpan.FromSeconds(3)))
                    .ShouldThrow<Exception>()
                    .And.Message.Should()
                    .Contain("Connection failed");
            }, Materializer);
        }

        [Fact]
        public void Outgoing_TCP_stream_must_work_when_client_closes_write_then_remote_closes_write()
        {
            this.AssertAllStagesStopped(() =>
            {
                var testData = ByteString.FromBytes(new byte[] { 1, 2, 3, 4, 5 });
                var server = new Server(this);

                var tcpWriteProbe = new TcpWriteProbe(this);
                var tcpReadProbe = new TcpReadProbe(this);
                Source.FromPublisher(tcpWriteProbe.PublisherProbe)
                    .Via(Sys.TcpStream().OutgoingConnection(server.Address))
                    .To(Sink.FromSubscriber(tcpReadProbe.SubscriberProbe))
                    .Run(Materializer);
                var serverConnection = server.WaitAccept();

                // Client can still write
                tcpWriteProbe.Write(testData);
                serverConnection.Read(5);
                serverConnection.WaitRead().ShouldBeEquivalentTo(testData);

                // Close client side write
                tcpWriteProbe.Close();
                serverConnection.ExpectClosed(Akka.IO.Tcp.PeerClosed.Instance);

                // Server can still write
                serverConnection.Write(testData);
                tcpReadProbe.Read(5).ShouldBeEquivalentTo(testData);

                // Close server side write
                serverConnection.ConfirmedClose();
                tcpReadProbe.SubscriberProbe.ExpectComplete();

                serverConnection.ExpectClosed(Akka.IO.Tcp.ConfirmedClosed.Instance);
                serverConnection.ExpectTerminated();
            }, Materializer);
        }

        [Fact]
        public void Outgoing_TCP_stream_must_work_when_remote_closes_write_then_client_closes_write()
        {
            this.AssertAllStagesStopped(() =>
            {
                var testData = ByteString.FromBytes(new byte[] {1, 2, 3, 4, 5});
                var server = new Server(this);

                var tcpWriteProbe = new TcpWriteProbe(this);
                var tcpReadProbe = new TcpReadProbe(this);
                Source.FromPublisher(tcpWriteProbe.PublisherProbe)
                    .Via(Sys.TcpStream().OutgoingConnection(server.Address))
                    .To(Sink.FromSubscriber(tcpReadProbe.SubscriberProbe))
                    .Run(Materializer);
                var serverConnection = server.WaitAccept();

                // Server can still write
                serverConnection.Write(testData);
                tcpReadProbe.Read(5).ShouldBeEquivalentTo(testData);

                // Close server side write
                serverConnection.ConfirmedClose();
                tcpReadProbe.SubscriberProbe.ExpectComplete();

                // Client can still write
                tcpWriteProbe.Write(testData);
                serverConnection.Read(5);
                serverConnection.WaitRead().ShouldBeEquivalentTo(testData);

                // Close client side write
                tcpWriteProbe.Close();
                serverConnection.ExpectClosed(Akka.IO.Tcp.ConfirmedClosed.Instance);
                serverConnection.ExpectTerminated();
            }, Materializer);
        }

        [Fact(Skip = "FIXME: actually this is about half-open connection. No other .NET socket lib supports that")]
        public void Outgoing_TCP_stream_must_work_when_client_closes_read_then_client_closes_write()
        {
            this.AssertAllStagesStopped(() =>
            {
                var testData = ByteString.FromBytes(new byte[] { 1, 2, 3, 4, 5 });
                var server = new Server(this);

                var tcpWriteProbe = new TcpWriteProbe(this);
                var tcpReadProbe = new TcpReadProbe(this);
                Source.FromPublisher(tcpWriteProbe.PublisherProbe)
                    .Via(Sys.TcpStream().OutgoingConnection(server.Address))
                    .To(Sink.FromSubscriber(tcpReadProbe.SubscriberProbe))
                    .Run(Materializer);
                var serverConnection = server.WaitAccept();

                // Server can still write
                serverConnection.Write(testData);
                tcpReadProbe.Read(5).ShouldBeEquivalentTo(testData);

                // Close client side read
                tcpReadProbe.TcpReadSubscription.Value.Cancel();

                // Client can still write
                tcpWriteProbe.Write(testData);
                serverConnection.Read(5);
                serverConnection.WaitRead().ShouldBeEquivalentTo(testData);

                // Close client side write
                tcpWriteProbe.Close();

                // Need a write on the server side to detect the close event
                AwaitAssert(() =>
                {
                    serverConnection.Write(testData);
                    serverConnection.ExpectClosed(c => c.IsErrorClosed, TimeSpan.FromMilliseconds(500));
                }, TimeSpan.FromSeconds(5));

                serverConnection.ExpectTerminated();
            }, Materializer);
        }

        [Fact]
        public void Outgoing_TCP_stream_must_work_when_client_closes_read_then_server_closes_write_then_client_closes_write()
        {
            this.AssertAllStagesStopped(() =>
            {
                var testData = ByteString.FromBytes(new byte[] { 1, 2, 3, 4, 5 });
                var server = new Server(this);

                var tcpWriteProbe = new TcpWriteProbe(this);
                var tcpReadProbe = new TcpReadProbe(this);
                Source.FromPublisher(tcpWriteProbe.PublisherProbe)
                    .Via(Sys.TcpStream().OutgoingConnection(server.Address))
                    .To(Sink.FromSubscriber(tcpReadProbe.SubscriberProbe))
                    .Run(Materializer);
                var serverConnection = server.WaitAccept();

                // Server can still write
                serverConnection.Write(testData);
                tcpReadProbe.Read(5).ShouldBeEquivalentTo(testData);

                // Close client side read
                tcpReadProbe.TcpReadSubscription.Value.Cancel();

                // Client can still write
                tcpWriteProbe.Write(testData);
                serverConnection.Read(5);
                serverConnection.WaitRead().ShouldBeEquivalentTo(testData);

                serverConnection.ConfirmedClose();

                // Close client side write
                tcpWriteProbe.Close();
                serverConnection.ExpectClosed(Akka.IO.Tcp.ConfirmedClosed.Instance);
                serverConnection.ExpectTerminated();
            }, Materializer);
        }

        [Fact]
        public void Outgoing_TCP_stream_must_shut_everything_down_if_client_signals_error()
        {
            this.AssertAllStagesStopped(() =>
            {
                var testData = ByteString.FromBytes(new byte[] { 1, 2, 3, 4, 5 });
                var server = new Server(this);

                var tcpWriteProbe = new TcpWriteProbe(this);
                var tcpReadProbe = new TcpReadProbe(this);
                Source.FromPublisher(tcpWriteProbe.PublisherProbe)
                    .Via(Sys.TcpStream().OutgoingConnection(server.Address))
                    .To(Sink.FromSubscriber(tcpReadProbe.SubscriberProbe))
                    .Run(Materializer);
                var serverConnection = server.WaitAccept();

                // Server can still write
                serverConnection.Write(testData);
                tcpReadProbe.Read(5).ShouldBeEquivalentTo(testData);
                
                // Client can still write
                tcpWriteProbe.Write(testData);
                serverConnection.Read(5);
                serverConnection.WaitRead().ShouldBeEquivalentTo(testData);

                // Cause error
                tcpWriteProbe.TcpWriteSubscription.Value.SendError(new IllegalStateException("test"));

                tcpReadProbe.SubscriberProbe.ExpectError();
                serverConnection.ExpectClosed(c=>c.IsErrorClosed);
                serverConnection.ExpectTerminated();
            }, Materializer);
        }

        [Fact]
        public void Outgoing_TCP_stream_must_shut_everything_down_if_client_signals_error_after_remote_has_closed_write()
        {
            this.AssertAllStagesStopped(() =>
            {
                var testData = ByteString.FromBytes(new byte[] { 1, 2, 3, 4, 5 });
                var server = new Server(this);

                var tcpWriteProbe = new TcpWriteProbe(this);
                var tcpReadProbe = new TcpReadProbe(this);
                Source.FromPublisher(tcpWriteProbe.PublisherProbe)
                    .Via(Sys.TcpStream().OutgoingConnection(server.Address))
                    .To(Sink.FromSubscriber(tcpReadProbe.SubscriberProbe))
                    .Run(Materializer);
                var serverConnection = server.WaitAccept();

                // Server can still write
                serverConnection.Write(testData);
                tcpReadProbe.Read(5).ShouldBeEquivalentTo(testData);

                // Close remote side write
                serverConnection.ConfirmedClose();
                tcpReadProbe.SubscriberProbe.ExpectComplete();

                // Client can still write
                tcpWriteProbe.Write(testData);
                serverConnection.Read(5);
                serverConnection.WaitRead().ShouldBeEquivalentTo(testData);
                
                tcpWriteProbe.TcpWriteSubscription.Value.SendError(new IllegalStateException("test"));
                serverConnection.ExpectClosed(c => c.IsErrorClosed);
                serverConnection.ExpectTerminated();
            }, Materializer);
        }

        [Fact]
        public void Outgoing_TCP_stream_must_shut_down_both_streams_when_connection_is_aborted_remotely()
        {
            this.AssertAllStagesStopped(() =>
            {
                // Client gets a PeerClosed event and does not know that the write side is also closed
                var server = new Server(this);

                var tcpWriteProbe = new TcpWriteProbe(this);
                var tcpReadProbe = new TcpReadProbe(this);

                Source.FromPublisher(tcpWriteProbe.PublisherProbe)
                    .Via(Sys.TcpStream().OutgoingConnection(server.Address))
                    .To(Sink.FromSubscriber(tcpReadProbe.SubscriberProbe))
                    .Run(Materializer);
                var serverConnection = server.WaitAccept();

                serverConnection.Abort();
                tcpReadProbe.SubscriberProbe.ExpectSubscriptionAndError();
                tcpWriteProbe.TcpWriteSubscription.Value.ExpectCancellation();

                serverConnection.ExpectTerminated();
            }, Materializer);
        }

        [Fact]
        public async Task Outgoing_TCP_stream_must_materialize_correctly_when_used_in_multiple_flows()
        {
            var testData = ByteString.FromBytes(new byte[] {1, 2, 3, 4, 5});
            var server = new Server(this);

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
            var serverConnection1 = server.WaitAccept();
            var conn2F = Source.FromPublisher(tcpWriteProbe2.PublisherProbe)
                .ViaMaterialized(outgoingConnection, Keep.Both)
                .To(Sink.FromSubscriber(tcpReadProbe2.SubscriberProbe))
                .Run(Materializer).Item2;
            var serverConnection2 = server.WaitAccept();

            ValidateServerClientCommunication(testData, serverConnection1, tcpReadProbe1, tcpWriteProbe1);
            ValidateServerClientCommunication(testData, serverConnection2, tcpReadProbe2, tcpWriteProbe2);
            
            var conn1 = await conn1F;
            var conn2 = await conn2F;

            // Since we have already communicated over the connections we can have short timeouts for the tasks
            ((IPEndPoint) conn1.RemoteAddress).Port.Should().Be(((IPEndPoint) server.Address).Port);
            ((IPEndPoint) conn2.RemoteAddress).Port.Should().Be(((IPEndPoint) server.Address).Port);
            ((IPEndPoint) conn1.LocalAddress).Port.Should().NotBe(((IPEndPoint) conn2.LocalAddress).Port);

            tcpWriteProbe1.Close();
            tcpReadProbe1.Close();

            server.Close();
        }

        [Fact]
        public void Outgoing_TCP_stream_must_properly_full_close_if_requested()
        {
            this.AssertAllStagesStopped(() =>
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
                var binding = task.Result;

                var t = Source.Maybe<ByteString>()
                    .Via(Sys.TcpStream().OutgoingConnection(serverAddress.Address.ToString(), serverAddress.Port))
                    .ToMaterialized(Sink.Aggregate<ByteString, ByteString>(ByteString.Empty, (s, s1) => s + s1), Keep.Both)
                    .Run(Materializer);
                var promise = t.Item1;
                var result = t.Item2;

                result.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
                result.Result.ShouldBeEquivalentTo(ByteString.FromString("Early response"));

                promise.SetResult(null); // close client upstream, no more data
                binding.Unbind();

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
                .RunAggregate(0, (i, s) => i + s.Count, Materializer);
            
            result.Should().Be(1000);

            await binding.Unbind();
        }

        [Fact]
        public async Task Outgoing_TCP_stream_must_handle_when_connection_actor_terminates_unexpectedly()
        {
            var system2 = ActorSystem.Create("system2", Sys.Settings.Config);
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
            Thread.Sleep(20);

            // Getting rid of existing connection actors by using a blunt instrument
            system2.ActorSelection(system2.Tcp().Path / "$a" / "*").Tell(Kill.Instance);
            result.Invoking(r => r.Wait()).ShouldThrow<StreamTcpException>();

            await binding.Result.Unbind();
            await system2.Terminate();
        }

        [Fact]
        public async Task Outgoing_TCP_stream_must_not_thrown_on_unbind_after_system_has_been_shut_down()
        {
            var sys2 = ActorSystem.Create("shutdown-test-system", Sys.Settings.Config);
            var mat2 = sys2.Materializer();

            try
            {
                var address = TestUtils.TemporaryServerAddress();
                var binding = await sys2.TcpStream()
                    .BindAndHandle(Flow.Create<ByteString>(), mat2, address.Address.ToString(), address.Port);

                // Ensure server is running
                // and is possible to communicate with 
                await Source.Single(ByteString.FromString(""))
                    .Via(sys2.TcpStream().OutgoingConnection(address))
                    .RunWith(Sink.Ignore<ByteString>(), mat2);

                await sys2.Terminate();
                await binding.Unbind();
            }
            finally
            {
                await sys2.Terminate();
            }
        }

        private void ValidateServerClientCommunication(ByteString testData, ServerConnection serverConnection, TcpReadProbe readProbe, TcpWriteProbe writeProbe)
        {
            serverConnection.Write(testData);
            serverConnection.Read(5);
            readProbe.Read(5).ShouldBeEquivalentTo(testData);
            writeProbe.Write(testData);
            serverConnection.WaitRead().ShouldBeEquivalentTo(testData);
        }
        
        private Sink<Tcp.IncomingConnection, Task> EchoHandler() =>
            Sink.ForEach<Tcp.IncomingConnection>(c => c.Flow.Join(Flow.Create<ByteString>()).Run(Materializer));

        [Fact]
        public async Task Tcp_listen_stream_must_be_able_to_implement_echo()
        {
            var serverAddress = TestUtils.TemporaryServerAddress();
            var t = Sys.TcpStream()
                .Bind(serverAddress.Address.ToString(), serverAddress.Port)
                .ToMaterialized(EchoHandler(), Keep.Both)
                .Run(Materializer);

            // make sure that the server has bound to the socket
            var binding = await t.Item1;
            var echoServerFinish = t.Item2;

            var testInput = Enumerable.Range(0, 255)
                .Select(i => ByteString.FromBytes(new[] {Convert.ToByte(i)}))
                .ToList();

            var expectedOutput = testInput.Aggregate(ByteString.Empty, (agg, b) => agg.Concat(b));

            var result = await Source.From(testInput)
                    .Via(Sys.TcpStream().OutgoingConnection(serverAddress))
                    .RunAggregate(ByteString.Empty, (agg, b) => agg.Concat(b), Materializer);
            
            result.ShouldBeEquivalentTo(expectedOutput);
            await binding.Unbind();
            await echoServerFinish;
        }

        [Fact]
        public async Task Tcp_listen_stream_must_work_with_a_chain_of_echoes()
        {
            var serverAddress = TestUtils.TemporaryServerAddress();
            var t = Sys.TcpStream()
                .Bind(serverAddress.Address.ToString(), serverAddress.Port)
                .ToMaterialized(EchoHandler(), Keep.Both)
                .Run(Materializer);

            // make sure that the server has bound to the socket
            var binding = await t.Item1;
            var echoServerFinish = t.Item2;

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
                .RunAggregate(ByteString.Empty, (agg, b) => agg.Concat(b), Materializer);
            
            result.ShouldBeEquivalentTo(expectedOutput);
            await binding.Unbind();
            await echoServerFinish;
        }

        [Fact]
        public void Tcp_listen_stream_must_bind_and_unbind_correctly()
        {
            // in JVM we filter for BindException, on .NET it's SocketException
            EventFilter.Exception<SocketException>().Expect(2, () =>
            {
                var address = TestUtils.TemporaryServerAddress();
                var probe1 = this.CreateSubscriberProbe<Tcp.IncomingConnection>();
                var bind = Sys.TcpStream().Bind(address.Address.ToString(), address.Port);

                // bind suceed, we have local address
                var binding1 = bind.To(Sink.FromSubscriber(probe1)).Run(Materializer).Result;

                probe1.ExpectSubscription();

                var probe2 = this.CreateManualSubscriberProbe<Tcp.IncomingConnection>();
                var binding2F = bind.To(Sink.FromSubscriber(probe2)).Run(Materializer);
                probe2.ExpectSubscriptionAndError().Should().BeOfType<BindFailedException>();

                var probe3 = this.CreateManualSubscriberProbe<Tcp.IncomingConnection>();
                var binding3F = bind.To(Sink.FromSubscriber(probe3)).Run(Materializer);
                probe3.ExpectSubscriptionAndError().Should().BeOfType<BindFailedException>();
                
                binding2F.Invoking(x => x.Wait(TimeSpan.FromSeconds(3))).ShouldThrow<BindFailedException>();
                binding3F.Invoking(x => x.Wait(TimeSpan.FromSeconds(3))).ShouldThrow<BindFailedException>();
                
                // Now unbind first
                binding1.Unbind().Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                probe1.ExpectComplete();

                var probe4 = this.CreateManualSubscriberProbe<Tcp.IncomingConnection>();
                // bind succeeded, we have local address
                var binding4Task = bind.To(Sink.FromSubscriber(probe4)).Run(Materializer);
                binding4Task.Wait(TimeSpan.FromSeconds(3));
                var binding4 = binding4Task.Result;
                probe4.ExpectSubscription();

                // clean up
                binding4.Unbind().Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
            });
        }

        [Fact(Skip = "FIXME: unexpected ErrorClosed")]
        public void Tcp_listen_stream_must_not_shut_down_connections_after_the_connection_stream_cancelled()
        {
            this.AssertAllStagesStopped(() =>
            {
                var thousandByteStrings = Enumerable.Range(0, 1000)
                    .Select(_ => ByteString.FromBytes(new byte[] { 0 }))
                    .ToArray();

                var serverAddress = TestUtils.TemporaryServerAddress();
                var t = Sys.TcpStream()
                    .Bind(serverAddress.Address.ToString(), serverAddress.Port)
                    .Take(1)
                    .ToMaterialized(Sink.ForEach<Tcp.IncomingConnection>(tcp =>
                    {
                        Thread.Sleep(1000); // we're testing here to see if it survives such race
                        tcp.Flow.Join(Flow.Create<ByteString>()).Run(Materializer);
                    }), Keep.Both)
                    .Run(Materializer);

                var bindingTask = t.Item1;

                // make sure server is running first
                bindingTask.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                var result = bindingTask.Result;

                // then connect, should trigger a block and then
                var total = Source.From(thousandByteStrings)
                    .Via(Sys.TcpStream().OutgoingConnection(serverAddress))
                    .RunAggregate(0, (i, s) => i + s.Count, Materializer);

                total.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
                total.Result.Should().Be(1000);
            }, Materializer);
        }

        [Fact(Skip="FIXME")]
        public void Tcp_listen_stream_must_shut_down_properly_even_if_some_accepted_connection_Flows_have_not_been_subscribed_to ()
        {
            this.AssertAllStagesStopped(() =>
            {
                var serverAddress = TestUtils.TemporaryServerAddress();
                var firstClientConnected = new TaskCompletionSource<NotUsed>();
                var takeTwoAndDropSecond = Flow.Create<Tcp.IncomingConnection>().Select(c =>
                {
                    firstClientConnected.TrySetResult(NotUsed.Instance);
                    return c;
                }).Grouped(2).Take(1).Select(e => e.First());

                Sys.TcpStream()
                    .Bind(serverAddress.Address.ToString(), serverAddress.Port)
                    .Via(takeTwoAndDropSecond)
                    .RunForeach(c => c.Flow.Join(Flow.Create<ByteString>()).Run(Materializer), Materializer);

                var folder = Source.From(Enumerable.Range(0, 100).Select(_ => ByteString.FromBytes(new byte[] {0})))
                    .Via(Sys.TcpStream().OutgoingConnection(serverAddress))
                    .Aggregate(0, (i, s) => i + s.Count)
                    .ToMaterialized(Sink.First<int>(), Keep.Right);

                var total = folder.Run(Materializer);

                firstClientConnected.Task.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
                var rejected = folder.Run(Materializer);

                total.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
                total.Result.Should().Be(100);

                rejected.Invoking(x => x.Wait(5.Seconds())).ShouldThrow<StreamTcpException>();
            }, Materializer);
        }
    }
}
