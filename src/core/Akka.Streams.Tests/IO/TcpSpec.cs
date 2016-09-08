//-----------------------------------------------------------------------
// <copyright file="TcpSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.IO;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using Tcp = Akka.Streams.Dsl.Tcp;

namespace Akka.Streams.Tests.IO
{
    public class TcpSpec : TcpHelper
    {
        public TcpSpec(ITestOutputHelper helper) : base("akka.stream.materializer.subscription-timeout.timeout = 2s", helper)
        {
        }

        [Fact(Skip="Fix me")]
        public void Outgoing_TCP_stream_must_work_in_the_happy_case()
        {
            this.AssertAllStagesStopped(() =>
            {
                var testData = ByteString.Create(new byte[] {1, 2, 3, 4, 5});

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

        [Fact(Skip = "Fix me")]
        public void Outgoing_TCP_stream_must_be_able_to_write_a_sequence_of_ByteStrings()
        {
            var server = new Server(this);
            var testInput = Enumerable.Range(0, 256).Select(i => ByteString.Create(new[] {Convert.ToByte(i)}));
            var expectedOutput = ByteString.Create(Enumerable.Range(0, 256).Select(Convert.ToByte).ToArray());

            Source.From(testInput)
                .Via(Sys.TcpStream().OutgoingConnection(server.Address))
                .To(Sink.Ignore<ByteString>())
                .Run(Materializer);

            var serverConnection = server.WaitAccept();
            serverConnection.Read(256);
            serverConnection.WaitRead().ShouldBeEquivalentTo(expectedOutput);
        }
        
        [Fact(Skip = "Fix me")]
        public void Outgoing_TCP_stream_must_be_able_to_read_a_sequence_of_ByteStrings()
        {
            var server = new Server(this);
            var testInput = Enumerable.Range(0, 255).Select(i => ByteString.Create(new[] { Convert.ToByte(i) }));
            var expectedOutput = ByteString.Create(Enumerable.Range(0, 255).Select(Convert.ToByte).ToArray());

            var idle = new TcpWriteProbe(this); //Just register an idle upstream
            var resultFuture =
                Source.FromPublisher(idle.PublisherProbe)
                    .Via(Sys.TcpStream().OutgoingConnection(server.Address))
                    .RunAggregate(ByteString.Empty, (acc, input) => acc + input, Materializer);
            var serverConnection = server.WaitAccept();

            foreach (var input in testInput)
                serverConnection.Write(input);

            serverConnection.ConfirmedClose();
            resultFuture.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            resultFuture.Result.ShouldBeEquivalentTo(expectedOutput);
        }

        [Fact(Skip = "Fix me")]
        public void Outgoing_TCP_stream_must_work_when_client_closes_write_then_remote_closes_write()
        {
            this.AssertAllStagesStopped(() =>
            {
                var testData = ByteString.Create(new byte[] { 1, 2, 3, 4, 5 });
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

        [Fact(Skip = "Fix me")]
        public void Outgoing_TCP_stream_must_work_when_remote_closes_write_then_client_closes_write()
        {
            this.AssertAllStagesStopped(() =>
            {
                var testData = ByteString.Create(new byte[] {1, 2, 3, 4, 5});
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

                // Close clint side write
                tcpWriteProbe.Close();
                serverConnection.ExpectClosed(Akka.IO.Tcp.ConfirmedClosed.Instance);
                serverConnection.ExpectTerminated();
            }, Materializer);
        }

        [Fact(Skip = "Fix me")]
        public void Outgoing_TCP_stream_must_work_when_client_closes_read_then_client_closes_write()
        {
            this.AssertAllStagesStopped(() =>
            {
                var testData = ByteString.Create(new byte[] { 1, 2, 3, 4, 5 });
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
                    serverConnection.ExpectClosed(c=>c.IsErrorClosed, TimeSpan.FromMilliseconds(500));
                }, TimeSpan.FromSeconds(5));

                serverConnection.ExpectTerminated();
            }, Materializer);
        }

        [Fact(Skip = "Fix me")]
        public void Outgoing_TCP_stream_must_work_when_client_closes_read_then_server_closes_write_then_client_closes_write()
        {
            this.AssertAllStagesStopped(() =>
            {
                var testData = ByteString.Create(new byte[] { 1, 2, 3, 4, 5 });
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

                // Close clint side write
                tcpWriteProbe.Close();
                serverConnection.ExpectClosed(Akka.IO.Tcp.ConfirmedClosed.Instance);
                serverConnection.ExpectTerminated();
            }, Materializer);
        }

        [Fact(Skip = "Fix me")]
        public void Outgoing_TCP_stream_must_shut_everything_down_if_client_signals_error()
        {
            this.AssertAllStagesStopped(() =>
            {
                var testData = ByteString.Create(new byte[] { 1, 2, 3, 4, 5 });
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

        [Fact(Skip = "Fix me")]
        public void Outgoing_TCP_stream_must_shut_everything_down_if_client_signals_error_after_remote_has_closed_write()
        {
            this.AssertAllStagesStopped(() =>
            {
                var testData = ByteString.Create(new byte[] { 1, 2, 3, 4, 5 });
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

        [Fact(Skip = "Fix me")]
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

        [Fact(Skip = "Fix me")]
        public void Outgoing_TCP_stream_must_materialize_correctly_when_used_in_multiple_flows()
        {
            var testData = ByteString.Create(new byte[] { 1, 2, 3, 4, 5 });
            var server = new Server(this);

            var tcpWriteProbe1 = new TcpWriteProbe(this);
            var tcpReadProbe1 = new TcpReadProbe(this);
            var tcpWriteProbe2 = new TcpWriteProbe(this);
            var tcpReadProbe2 = new TcpReadProbe(this);
            var outgoingConnection = new Tcp().CreateExtension(Sys as ExtendedActorSystem).OutgoingConnection(server.Address);

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

            conn1F.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
            conn2F.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
            var conn1 = conn1F.Result;
            var conn2 = conn2F.Result;

            // Since we have already communicated over the connections we can have short timeouts for the tasks
            ((IPEndPoint) conn1.RemoteAddress).Port.Should().Be(((IPEndPoint) server.Address).Port);
            ((IPEndPoint) conn2.RemoteAddress).Port.Should().Be(((IPEndPoint) server.Address).Port);
            ((IPEndPoint) conn1.LocalAddress).Port.Should().NotBe(((IPEndPoint) conn2.LocalAddress).Port);

            tcpWriteProbe1.Close();
            tcpReadProbe1.Close();

            server.Close();
        }

        [Fact(Skip = "Fix me")]
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
                task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                var binding = task.Result;

                var t = Source.Maybe<ByteString>()
                    .Via(Sys.TcpStream().OutgoingConnection(serverAddress.Address.ToString(), serverAddress.Port))
                    .ToMaterialized(Sink.Aggregate<ByteString, ByteString>(ByteString.Empty, (s, s1) => s + s1), Keep.Both)
                    .Run(Materializer);
                var promise = t.Item1;
                var result = t.Item2;

                result.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                result.Result.ShouldBeEquivalentTo(ByteString.FromString("Early response"));

                promise.SetResult(null); // close client upstream, no more data
                binding.Unbind();

            }, Materializer);
        }

        [Fact(Skip = "Fix me")]
        public void Outgoing_TCP_stream_must_Echo_should_work_even_if_server_is_in_full_close_mode()
        {
            var serverAddress = TestUtils.TemporaryServerAddress();

            var task = Sys.TcpStream()
                    .Bind(serverAddress.Address.ToString(), serverAddress.Port, halfClose: false)
                    .ToMaterialized(
                        Sink.ForEach<Tcp.IncomingConnection>(conn => conn.Flow.Join(Flow.Create<ByteString>())),
                        Keep.Left)
                    .Run(Materializer);
            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            var binding = task.Result;

            var result = Source.From(Enumerable.Repeat(0, 1000)
                .Select(i => ByteString.Create(new[] {Convert.ToByte(i)})))
                .Via(Sys.TcpStream().OutgoingConnection(serverAddress))
                .RunAggregate(0, (i, s) => i + s.Count, Materializer);

            result.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            result.Result.Should().Be(1000);

            binding.Unbind();
        }

        [Fact(Skip = "Fix me")]
        public void Outgoing_TCP_stream_must_handle_when_connection_actor_terminates_unexpectedly()
        {
            var system2 = ActorSystem.Create("system2");
            var mat2 = ActorMaterializer.Create(system2);

            var serverAddress = TestUtils.TemporaryServerAddress();
            var binding = system2.TcpStream()
                .BindAndHandle(Flow.Create<ByteString>(), mat2, serverAddress.Address.ToString(), serverAddress.Port);

            var result = Source.Maybe<ByteString>()
                .Via(system2.TcpStream().OutgoingConnection(serverAddress))
                .RunAggregate(0, (i, s) => i + s.Count, mat2);

            // Getting rid of existing connection actors by using a blunt instrument
            system2.ActorSelection(system2.Tcp().Path/"selectors"/"$b"/"*").Tell(Kill.Instance);
            result.Invoking(r => r.Wait(TimeSpan.FromSeconds(3))).ShouldThrow<StreamTcpException>();

            binding.Result.Unbind().Wait();
            system2.Terminate().Wait();
        }

        [Fact(Skip = "Fix me")]
        public void Outgoing_TCP_stream_must_not_thrown_on_unbind_after_system_has_been_shut_down()
        {
            var sys2 = ActorSystem.Create("shutdown-test-system");
            var mat2 = sys2.Materializer();

            try
            {
                var address = TestUtils.TemporaryServerAddress();
                var bindingTask = sys2.TcpStream()
                    .BindAndHandle(Flow.Create<ByteString>(), mat2, address.Address.ToString(), address.Port);

                // Ensure server is running
                var t = Source.Single(ByteString.FromString(""))
                    .Via(sys2.TcpStream().OutgoingConnection(address))
                    .RunWith(Sink.Ignore<ByteString>(), mat2);
                t.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();

                sys2.Terminate().Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();

                bindingTask.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                var binding = bindingTask.Result;
                binding.Unbind().Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            }
            finally
            {
                sys2.Terminate().Wait(TimeSpan.FromSeconds(5));
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

        [Fact(Skip = "Fix me")]
        public void Tcp_listen_stream_must_be_able_to_implement_echo()
        {
            var serverAddress = TestUtils.TemporaryServerAddress();
            var t = Sys.TcpStream()
                .Bind(serverAddress.Address.ToString(), serverAddress.Port)
                .ToMaterialized(EchoHandler(), Keep.Both)
                .Run(Materializer);
            var bindingFuture = t.Item1;
            var echoServerFinish = t.Item2;

            // make sure that the server has bound to the socket
            bindingFuture.Wait(100).Should().BeTrue();
            var binding = bindingFuture.Result;

            var testInput = Enumerable.Range(0, 255).Select(i => ByteString.Create(new[] {Convert.ToByte(i)})).ToList();
            var expectedOutput = testInput.Aggregate(ByteString.Empty, (agg, b) => agg.Concat(b));
            var resultFuture =
                Source.From(testInput)
                    .Via(Sys.TcpStream().OutgoingConnection(serverAddress))
                    .RunAggregate(ByteString.Empty, (agg, b) => agg.Concat(b), Materializer);

            resultFuture.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            resultFuture.Result.ShouldBeEquivalentTo(expectedOutput);
            binding.Unbind().Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            echoServerFinish.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
        }

        [Fact(Skip = "Fix me")]
        public void Tcp_listen_stream_must_work_with_a_chain_of_echoes()
        {
            var serverAddress = TestUtils.TemporaryServerAddress();
            var t = Sys.TcpStream()
                .Bind(serverAddress.Address.ToString(), serverAddress.Port)
                .ToMaterialized(EchoHandler(), Keep.Both)
                .Run(Materializer);
            var bindingFuture = t.Item1;
            var echoServerFinish = t.Item2;
            
            // make sure that the server has bound to the socket
            bindingFuture.Wait(100).Should().BeTrue();
            var binding = bindingFuture.Result;

            var echoConnection = Sys.TcpStream().OutgoingConnection(serverAddress);

            var testInput = Enumerable.Range(0, 255).Select(i => ByteString.Create(new[] { Convert.ToByte(i) })).ToList();
            var expectedOutput = testInput.Aggregate(ByteString.Empty, (agg, b) => agg.Concat(b));

            var resultFuture = Source.From(testInput)
                .Via(echoConnection) // The echoConnection is reusable
                .Via(echoConnection)
                .Via(echoConnection)
                .Via(echoConnection)
                .RunAggregate(ByteString.Empty, (agg, b) => agg.Concat(b), Materializer);

            resultFuture.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            resultFuture.Result.ShouldBeEquivalentTo(expectedOutput);
            binding.Unbind().Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            echoServerFinish.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
        }

        [Fact(Skip = "On Windows unbinding is not immediate")]
        public void Tcp_listen_stream_must_bind_and_unbind_correctly()
        {
            EventFilter.Exception<BindFailedException>().Expect(2, () =>
            {
                  // if (Helpers.isWindows) {
                  //  info("On Windows unbinding is not immediate")
                  //  pending
                  //}
                  //val address = temporaryServerAddress()
                  //val probe1 = TestSubscriber.manualProbe[Tcp.IncomingConnection]()
                  //val bind = Tcp(system).bind(address.getHostName, address.getPort) // TODO getHostString in Java7
                  //// Bind succeeded, we have a local address
                  //val binding1 = Await.result(bind.to(Sink.fromSubscriber(probe1)).run(), 3.second)

                  //probe1.expectSubscription()

                  //val probe2 = TestSubscriber.manualProbe[Tcp.IncomingConnection]()
                  //val binding2F = bind.to(Sink.fromSubscriber(probe2)).run()
                  //probe2.expectSubscriptionAndError(BindFailedException)

                  //val probe3 = TestSubscriber.manualProbe[Tcp.IncomingConnection]()
                  //val binding3F = bind.to(Sink.fromSubscriber(probe3)).run()
                  //probe3.expectSubscriptionAndError()

                  //a[BindFailedException] shouldBe thrownBy { Await.result(binding2F, 1.second) }
                  //a[BindFailedException] shouldBe thrownBy { Await.result(binding3F, 1.second) }

                  //// Now unbind first
                  //Await.result(binding1.unbind(), 1.second)
                  //probe1.expectComplete()

                  //val probe4 = TestSubscriber.manualProbe[Tcp.IncomingConnection]()
                  //// Bind succeeded, we have a local address
                  //val binding4 = Await.result(bind.to(Sink.fromSubscriber(probe4)).run(), 3.second)
                  //probe4.expectSubscription()

                  //// clean up
                  //Await.result(binding4.unbind(), 1.second)
            });
        }

        [Fact(Skip = "Fix me")]
        public void Tcp_listen_stream_must_not_shut_down_connections_after_the_connection_stream_cacelled()
        {
            this.AssertAllStagesStopped(() =>
            {
                var serverAddress = TestUtils.TemporaryServerAddress();
                Sys.TcpStream()
                    .Bind(serverAddress.Address.ToString(), serverAddress.Port)
                    .Take(1).RunForeach(c =>
                    {
                        Thread.Sleep(1000);
                        c.Flow.Join(Flow.Create<ByteString>()).Run(Materializer);
                    }, Materializer);

                var total = Source.From(
                    Enumerable.Range(0, 1000).Select(_ => ByteString.Create(new byte[] {0})))
                    .Via(Sys.TcpStream().OutgoingConnection(serverAddress))
                    .RunAggregate(0, (i, s) => i + s.Count, Materializer);

                total.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                total.Result.Should().Be(1000);
            }, Materializer);
        }

        [Fact(Skip = "Fix me")]
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

                var folder = Source.From(Enumerable.Range(0, 100).Select(_ => ByteString.Create(new byte[] {0})))
                    .Via(Sys.TcpStream().OutgoingConnection(serverAddress))
                    .Aggregate(0, (i, s) => i + s.Count)
                    .ToMaterialized(Sink.First<int>(), Keep.Right);

                var total = folder.Run(Materializer);

                firstClientConnected.Task.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
                var rejected = folder.Run(Materializer);

                total.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
                total.Result.Should().Be(100);

                rejected.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
                rejected.Exception.Flatten().InnerExceptions.Any(e => e is StreamTcpException).Should().BeTrue();
            }, Materializer);
        }
    }
}
