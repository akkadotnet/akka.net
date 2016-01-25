//-----------------------------------------------------------------------
// <copyright file="TcpConnectionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.IO;
using Akka.Pattern;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;

namespace Akka.Tests.IO
{
    class TcpConnectionSpec : AkkaSpec
    {
        internal class Ack : Tcp.Event
        {
            private readonly int _i;

            public Ack(int i)
            {
                _i = i;
            }

            public static readonly Ack Instance = new Ack(0);

            public override bool Equals(object obj)
            {
                var other = obj as Ack;
                if (other == null) return false;
                return _i == other._i;
            }
        }

        private static string _connectionResetByPeerMessage;
        internal static string ConnectionResetByPeerMessage
        {
            get
            {
                return "An existing connection was forcibly closed by the remote host";
                //TODO: Implement
                if (_connectionResetByPeerMessage == null)
                {
                    var serverSocket = SocketChannel.Open();
                    serverSocket.Socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                    try
                    {
                        var clientSocket = SocketChannel.Open();
                        clientSocket.Socket.Connect(IPAddress.Loopback, ((IPEndPoint) serverSocket.Socket.LocalEndPoint).Port);

                    }
                    catch (Exception ex)
                    {
                        _connectionResetByPeerMessage = ex.Message;
                    }
                }
                return _connectionResetByPeerMessage;
            }
        }

        internal class Registration : INoSerializationVerificationNeeded
        {
            public SocketChannel Channel { get; set; }
            public SocketAsyncOperation? InitialOps { get; set; }

            public Registration(SocketChannel channel, SocketAsyncOperation? initialOps)
            {
                Channel = channel;
                InitialOps = initialOps;
            }
        }


        public TcpConnectionSpec()
            : base(@"akka.io.tcp.register-timeout = 500ms
                     akka.io.tcp.max-received-message-size = 1024
                     akka.io.tcp.direct-buffer-size = 512
                     akka.actor.serialize-creators = on
                    ")
        { }


        [Fact]
        public void An_Outgoing_Connection_Must_Set_SocketOptions_Before_Connecting()
        {
            new LocalServerTest(this).Run(x =>
            {
                var connectionActor = x.CreateConnectionActor(new Inet.SO.ReuseAddress(true));
                var clientChannel = connectionActor.UnderlyingActor.Channel;
                Assert.NotEqual(0,
                    clientChannel.Socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress));
            });
        }

        [Fact]
        public void An_Outgoing_Connection_Must_Set_SocketOptions_After_Connecting()
        {
            new LocalServerTest(this).Run(x =>
            {

            });
        }

        [Fact]
        public void An_Outgoing_Connection_Must_Send_Incoming_Data_To_The_Connection_Handler()
        {
            new EstablishedConnectionTest(this).Run(x =>
            {
                x.ServerSideChannel.Write(ByteBuffer.Wrap(Encoding.ASCII.GetBytes("testdata")));

                x.ExpectReceivedString("testdata");

                x.ServerSideChannel.Write(ByteBuffer.Wrap(Encoding.ASCII.GetBytes("testdata2")));
                x.ServerSideChannel.Write(ByteBuffer.Wrap(Encoding.ASCII.GetBytes("testdata3")));

                x.ExpectReceivedString("testdata2testdata3");
            });
        }

        [Fact]
        public void An_Outgoing_Connection_Must_Forward_Incoming_Data_As_Received_Message_Instantly_As_Long_As_More_Data_Is_Available()
        {
            new EstablishedConnectionTest(this).Run(x =>
            {
                var bufferSize = Tcp.Instance.Apply(Sys).Settings.DirectBufferSize;
                var dataSize = bufferSize + 150;
                var bigData = new byte[dataSize];
                var buffer = ByteBuffer.Wrap(bigData);

                x.ServerSideChannel.Socket.SendBufferSize = 150000;
                var wrote = x.ServerSideChannel.Write(buffer);
                wrote.ShouldBe(dataSize);

                ExpectNoMsg(1000);

                x.Selector.Send(x.ConnectionActor, SelectionHandler.ChannelReadable.Instance);

                x.ConnectionHandler.ExpectMsg<Tcp.Received>(m => m.Data.Count == bufferSize);
                x.ConnectionHandler.ExpectMsg<Tcp.Received>(m => m.Data.Count == 150);
            });
        }

        [Fact]
        public void An_Outgoing_Connection_Must_Receive_Data_Directly_When_The_Connection_Is_Established()
        {
            new UnacceptedConnectionTest(this).Run(x =>
            {
                var serverSideChannel = x.AcceptServerSideChannel(x.LocalServerChannel);

                serverSideChannel.Write(ByteBuffer.Wrap(Encoding.ASCII.GetBytes("immediatedata")));


                // JVM Akka always excpect CONNECT, which seems incorrect
                // We will not receive a CONNECT if Socket.BeginConnect completed synchronously
                // We therfore just igenore the CONNECT if it is in the queue
                //What JVM Akka does:  InterestCallReceiver.ExpectMsg((int)SocketAsyncOperation.Connect); 
                if (x.InterestCallReceiver.ReceiveWhile<object>(m => m is int && (int)m == (int)SocketAsyncOperation.Connect, TimeSpan.Zero, TimeSpan.Zero, 1).Any())
                    x.Selector.Send(x.ConnectionActor, SelectionHandler.ChannelConnectable.Instance);   // Only send ChannelConnectable if we did not complete synchronously 
                
                x.UserHandler.ExpectMsg<Tcp.Connected>(); //TODO: assert message

                x.UserHandler.Send(x.ConnectionActor, new Tcp.Register(x.UserHandler.Ref));
                x.UserHandler.ExpectMsg<Tcp.Received>(m => m.Data.DecodeString(Encoding.ASCII) == "immediatedata");
                x.InterestCallReceiver.ExpectMsg((int) SocketAsyncOperation.Receive);
            });
        }

        [Fact]
        public void An_Outgoing_Connection_Must_Write_Data_To_Network_And_Acknowledge()
        {
            new EstablishedConnectionTest(this).Run(x =>
            {
                var writer = CreateTestProbe();

                var ackedWrite = Tcp.Write.Create(ByteString.FromString("testdata"), Ack.Instance);
                var buffer = ByteBuffer.Allocate(100);

                Assert.Equal(0, x.ServerSideChannel.Read(buffer));
                writer.Send(x.ConnectionActor, ackedWrite);
                writer.ExpectMsg(Ack.Instance);
                x.PullFromServerSide(remaining: 8, into: buffer, remainingRetries: 1000);
                buffer.Flip();

                //TODO: Buffer.limit
                //Assert.Equal(8, buffer.Limit); 

                var unackedWrite = Tcp.Write.Create(ByteString.FromString("morestuff!"), Tcp.NoAck.Instance);
                buffer.Clear();
                Assert.Equal(0, x.ServerSideChannel.Read(buffer));
                writer.Send(x.ConnectionActor, unackedWrite);
                writer.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
                x.PullFromServerSide(remaining: 10, into: buffer, remainingRetries: 1000);
                buffer.Flip();
                Assert.Equal("morestuff!", ByteString.Create(buffer).DecodeString(Encoding.UTF8));
            });
        }

        [Fact]
        public void An_Outgoing_Connection_Must_Send_Big_Buffers_To_Network_Correctly()
        {
            new EstablishedConnectionTest(this).Run(x =>
            {
                var bufferSize = 512*1024;
                var random = new Random(0);
                var testBytes = new byte[bufferSize];
                random.NextBytes(testBytes);
                var testData = ByteString.Create(testBytes, 0, testBytes.Length);

                var writer = CreateTestProbe();

                var write = Tcp.Write.Create(testData, Ack.Instance);
                var buffer = ByteBuffer.Allocate(bufferSize);
                x.ServerSideChannel.Read(buffer).ShouldBe(0);
                writer.Send(x.ConnectionActor, write);
                x.PullFromServerSide(remaining: bufferSize, into: buffer, remainingRetries: 1000);
                buffer.Flip();

                //TODO: Buffer.limit
                //Assert.Equal(buffer, buffer.Limit());

                ByteString.Create(buffer).ShouldBe(testData);
            });
        }

        [Fact]
        public void An_Outgoing_Connection_Must_Write_Data_After_Not_Acknowledged_Data()
        {
            new EstablishedConnectionTest(this).Run(x =>
            {
                var writer = CreateTestProbe();
                writer.Send(x.ConnectionActor,
                    Tcp.Write.Create(ByteString.Create(new byte[] {42}, 0, 1), Tcp.NoAck.Instance));
                writer.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
            });
        }

        [Fact]
        public void An_Outgoing_Connection_Must_Acknowledge_The_Completion_Of_An_ACKed_EmptyWrite()
        {
            new EstablishedConnectionTest(this).Run(x =>
            {
                var writer = CreateTestProbe();
                writer.Send(x.ConnectionActor, Tcp.Write.Create(ByteString.Empty, Ack.Instance));
                writer.ExpectMsg(Ack.Instance);
            });
        }

        [Fact]
        public void An_Outgoing_Connection_Must_Not_Acknowledge_The_Completion_Of_An_NACKed_EmptyWrite()
        {
            new EstablishedConnectionTest(this).Run(x =>
            {
                var writer = CreateTestProbe();
                writer.Send(x.ConnectionActor, Tcp.Write.Create(ByteString.Empty, Tcp.NoAck.Instance));
                writer.ExpectNoMsg(TimeSpan.FromMilliseconds(250));
                writer.Send(x.ConnectionActor, Tcp.Write.Create(ByteString.Empty, new Tcp.NoAck(42)));
                writer.ExpectNoMsg(TimeSpan.FromMilliseconds(250));
            });
        }

        [Fact]
        public void An_Outgoing_Connection_Must_Write_A_CompoundWrite_To_The_Network_And_Produce_Correct_ACKs()
        {
            new EstablishedConnectionTest(this).Run(x =>
            {
                var writer = CreateTestProbe();

                //TODO: Fix bug when empty write is used
                var compoundWrite = Tcp.CompoundWrite.Create(
                    Tcp.Write.Create(ByteString.FromString("test1"), new Ack(1)),
                    Tcp.Write.Create(ByteString.FromString("test2")),
                    //Tcp.Write.Create(ByteString.Empty, new Ack(3)),
                    Tcp.Write.Create(ByteString.FromString("test4"), new Ack(4)));

                var buffer = ByteBuffer.Allocate(100);
                x.ServerSideChannel.Read(buffer).ShouldBe(0);
                writer.Send(x.ConnectionActor, compoundWrite);

                x.PullFromServerSide(remaining: 15, into: buffer, remainingRetries: 1000);
                buffer.Flip();
                ByteString.Create(buffer).DecodeString(Encoding.UTF8).ShouldBe("test1test2test4");
                writer.ExpectMsg(new Ack(1));
                //writer.ExpectMsg(new Ack(3));
                writer.ExpectMsg(new Ack(4));
            });
        }

        [Fact]
        public void An_Outgoing_Connection_Must_Respect_StopReading_And_ResumeReading()
        {
            new EstablishedConnectionTest(this).Run(x =>
            {
                x.ServerSideChannel.Write(ByteBuffer.Wrap(Encoding.ASCII.GetBytes("testdata")));
                x.ConnectionHandler.Send(x.ConnectionActor, Tcp.SuspendReading.Instance);

                x.InterestCallReceiver.ExpectMsg(-(int) SocketAsyncOperation.Receive);

                x.Selector.Send(x.ConnectionActor, 1);

                x.InterestCallReceiver.ExpectNoMsg(100);
                x.ConnectionHandler.ExpectNoMsg(100);

                x.ConnectionHandler.Send(x.ConnectionActor, Tcp.ResumeReading.Instance);
                x.InterestCallReceiver.ExpectMsg((int) SocketAsyncOperation.Receive);

                x.ExpectReceivedString("testdata");
            });
        }

        [Fact]
        public void An_Outgoing_Connection_Must_Respect_PullMode()
        {
            new EstablishedConnectionTest(this, pullMode: true).Run(x =>
            {
                x.ServerSideChannel.Write(ByteBuffer.Wrap(Encoding.ASCII.GetBytes("testdata")));
                x.ConnectionHandler.ExpectNoMsg(100);

                x.ConnectionActor.Tell(Tcp.ResumeReading.Instance);
                x.InterestCallReceiver.ExpectMsg((int) SocketAsyncOperation.Receive);
                x.Selector.Send(x.ConnectionActor, SelectionHandler.ChannelReadable.Instance);
                x.ConnectionHandler.ExpectMsg<Tcp.Received>(m => m.Data.DecodeString(Encoding.ASCII) == "testdata");

                x.ServerSideChannel.Write(ByteBuffer.Wrap(Encoding.ASCII.GetBytes("testdata2")));
                x.ServerSideChannel.Write(ByteBuffer.Wrap(Encoding.ASCII.GetBytes("testdata3")));

                x.ConnectionHandler.ExpectNoMsg(100);

                x.ConnectionActor.Tell(Tcp.ResumeReading.Instance);
                x.InterestCallReceiver.ExpectMsg((int) SocketAsyncOperation.Receive);
                x.Selector.Send(x.ConnectionActor, SelectionHandler.ChannelReadable.Instance);
                x.ConnectionHandler.ExpectMsg<Tcp.Received>(
                    m => m.Data.DecodeString(Encoding.ASCII) == "testdata2testdata3");
            });
        }

        [Fact]
        public void An_Outgoing_Connection_Must_Close_The_Connection_And_Reply_With_Closed_Upon_Reception_Of_CloseCommand()
        {
            //TODO: SmallRcvBuffer
        }

        [Fact]
        public void An_Outgoing_Connection_Must_Send_Only_One_ClosedEvent_To_The_Handler_If_The_Handler_Commanded_The_Close()
        {
            new EstablishedConnectionTest(this).Run(x =>
            {
                x.ConnectionHandler.Send(x.ConnectionActor, Tcp.Close.Instance);
                x.ConnectionHandler.ExpectMsg(Tcp.Closed.Instance);
                x.ConnectionHandler.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
            });
        }

        [Fact]
        public void An_Outgoing_Connection_Must_Abort_The_Connection_And_Reply_With_Aborted_Upon_Reception_Of_An_AbortCommand()
        {
            new EstablishedConnectionTest(this).Run(x =>
            {
                x.ConnectionHandler.Send(x.ConnectionActor, Tcp.Abort.Instance);
                x.ConnectionHandler.ExpectMsg(Tcp.Aborted.Instance);

                x.AssertThisConnectionActorTerminated();

                var buffer = ByteBuffer.Allocate(1);
                var thrown = Assert.Throws<SocketException>(() =>
                {
                    x.ServerSideChannel.Read(buffer);
                });
                // TODO: declare ConnectionResetByPeerMessage
                thrown.Message.ShouldBe(ConnectionResetByPeerMessage);
            });
        }

        [Fact]
        public void An_Outgoing_Connection_Must_Report_When_Peer_Closed_The_Connection()
        {
            new EstablishedConnectionTest(this).Run(x =>
            {
                x.CloseServerSideAndWaitForClientReadable();

                x.Selector.Send(x.ConnectionActor, SelectionHandler.ChannelReadable.Instance);
                x.ConnectionHandler.ExpectMsg(Tcp.PeerClosed.Instance);

                x.AssertThisConnectionActorTerminated();
            });
        }

        [Fact]
        public void An_Outgoing_Connection_Must_Report_When_Peer_Closed_The_Connection_But_Allow_Further_Writes_And_Acknowledge_Normal_Close()
        {
            new EstablishedConnectionTest(this, keepOpenOnPeerClosed: true).Run(x =>
            {
                x.CloseServerSideAndWaitForClientReadable(fullClose: false);

                x.Selector.Send(x.ConnectionActor, SelectionHandler.ChannelReadable.Instance);
                x.ConnectionHandler.ExpectMsg(Tcp.PeerClosed.Instance);

                x.ConnectionHandler.Send(x.ConnectionActor, x.WriteCmd(Ack.Instance));
                x.PullFromServerSide(x.TestSize);
                x.ConnectionHandler.ExpectMsg(Ack.Instance);
                x.ConnectionHandler.Send(x.ConnectionActor, Tcp.Close.Instance);
                x.ConnectionHandler.ExpectMsg(Tcp.Closed.Instance);

                x.AssertThisConnectionActorTerminated();
            });
        }

        [Fact]
        public void An_Outgoing_Connection_Must_Report_When_Peer_Closed_The_Connection_But_Allow_Further_Writes_And_Acknowledge_Confirmed_Close()
        {
            new EstablishedConnectionTest(this, keepOpenOnPeerClosed: true).Run(x =>
            {
                x.CloseServerSideAndWaitForClientReadable(fullClose: false);

                x.Selector.Send(x.ConnectionActor, SelectionHandler.ChannelReadable.Instance);
                x.ConnectionHandler.ExpectMsg(Tcp.PeerClosed.Instance);

                x.ConnectionHandler.Send(x.ConnectionActor, x.WriteCmd(Ack.Instance));
                x.PullFromServerSide(x.TestSize);
                x.ConnectionHandler.ExpectMsg(Ack.Instance);
                x.ConnectionHandler.Send(x.ConnectionActor, Tcp.ConfirmedClose.Instance);
                x.ConnectionHandler.ExpectMsg(Tcp.ConfirmedClosed.Instance);

                x.AssertThisConnectionActorTerminated();
            });
        }

        [Fact]
        public void An_Outgoing_Connection_Must_Report_When_Peer_Aborted_The_Connection()
        {
            new EstablishedConnectionTest(this).Run(x =>
            {
                x.AbortClose(x.ServerSideChannel);
                x.Selector.Send(x.ConnectionActor, SelectionHandler.ChannelReadable.Instance);
                var err = x.ConnectionHandler.ExpectMsg<Tcp.ErrorClosed>();
                err.GetErrorCause().ShouldBe(ConnectionResetByPeerMessage);

                x.ConnectionHandler.ExpectNoMsg(200);

                x.AssertThisConnectionActorTerminated();
            });            
        }

        [Fact]
        public void An_Outgoing_Connection_Must_Report_When_Peer_Closed_The_Connection_When_Trying_To_Write()
        {
            new EstablishedConnectionTest(this).Run(x =>
            {
                var writer = CreateTestProbe();

                x.AbortClose(x.ServerSideChannel);
                writer.Send(x.ConnectionActor, Tcp.Write.Create(ByteString.FromString("testdata"), Ack.Instance));
                writer.ExpectMsg<Tcp.ErrorClosed>();
                x.ConnectionHandler.ExpectMsg<Tcp.ErrorClosed>();

                x.AssertThisConnectionActorTerminated();
            });            
        }

        [Fact]
        public void An_Outgoing_Connection_Must_Report_Failed_connection_attempt_when_target_is_unreachable()
        {
            var unboundAddress = TestUtils.TemporaryServerAddress();
            var test = new UnacceptedConnectionTest(this);
            test.ConnectionActor = test.CreateConnectionActor(serverAddress: unboundAddress);
            test.Run(x =>
            {
                //x.ClientSideChannel.Socket.Poll(3000, SelectMode.SelectWrite).ShouldBe(true);  // In .NET we cant select on 'Connectable' (I think)

                Thread.Sleep(3000);
                x.Selector.Send(x.ConnectionActor, SelectionHandler.ChannelConnectable.Instance);
                x.UserHandler.ExpectMsg<Tcp.CommandFailed>();

                Watch(x.ConnectionActor);
                ExpectTerminated(x.ConnectionActor);
            });
        }

        [Fact]
        public void An_Outgoing_Connection_Must_Report_Failed_connection_attempt_when_target_cannot_be_resolved()
        {
            var test = new UnacceptedConnectionTest(this);
            var address = new DnsEndPoint("notthere.local", 666);
            test.ConnectionActor = test.CreateConnectionActorWithoutRegistration(address);
            test.Run(x =>
            {
                x.ConnectionActor.Tell(x.NewChannelRegistration());
                x.UserHandler.ExpectMsg<Tcp.CommandFailed>(TimeSpan.FromSeconds(30));
            });
        }

        [Fact]
        public void An_Outgoing_Connection_Must_Report_Failed_connection_attempt_when_timing_out()
        {
            var unboundAddress = TestUtils.TemporaryServerAddress();
            var test = new UnacceptedConnectionTest(this);
            test.ConnectionActor = test.CreateConnectionActor(serverAddress: unboundAddress, timeout:TimeSpan.FromMilliseconds(100));
            test.Run(x =>
            {   
                x.UserHandler.ExpectMsg<Tcp.CommandFailed>();
                Watch(x.ConnectionActor);
                ExpectTerminated(x.ConnectionActor);
            });
        }

        [Fact]
        public void An_Outgoing_Connection_Must_time_out_when_Connected_isnt_answered_with_Register()
        {
            new UnacceptedConnectionTest(this).Run(x =>
            {
                x.LocalServerChannel.Socket.BeginAccept(ar => x.LocalServerChannel.Socket.EndAccept(ar), null);

                x.Selector.Send(x.ConnectionActor, SelectionHandler.ChannelConnectable.Instance);
                x.UserHandler.ExpectMsg<Tcp.Connected>();

                Watch(x.ConnectionActor.Ref);
                ExpectTerminated(x.ConnectionActor.Ref);
            });        
        }

        [Fact]
        public void An_Outgoing_Connection_Must_close_the_connection_when_user_handler_dies_while_connecting()
        {
            new UnacceptedConnectionTest(this).Run(x => EventFilter.Exception<DeathPactException>().ExpectOne(() =>
            {
                x.UserHandler.Ref.Tell(PoisonPill.Instance);
            
                Watch(x.ConnectionActor.Ref);
                ExpectTerminated(x.ConnectionActor.Ref);
            }));
        }

        [Fact]
        public void An_Outgoing_Connection_Must_close_the_connection_when_connection_handler_dies_while_connected()
        {
            new EstablishedConnectionTest(this).Run(x =>
            {
                {
                    Watch(x.ConnectionHandler.Ref);
                    Watch(x.ConnectionActor);
                    EventFilter.Exception<DeathPactException>().ExpectOne(() =>
                    {
                        Sys.Stop(x.ConnectionHandler.Ref);
                        var deaths = new[] {ExpectMsg<Terminated>().ActorRef, ExpectMsg<Terminated>().ActorRef};
                        deaths.ShouldOnlyContainInOrder(x.ConnectionHandler.Ref, x.ConnectionActor);
                    });
                }
            });
        }

        class Works : Tcp.Event {public static readonly Works Instance = new Works();}
        [Fact]
        public void An_Outgoing_Connection_Must_support_ResumeWriting_backed_up()
        {
            new EstablishedConnectionTest(this).Run(x =>
            {
                var writer = CreateTestProbe();
                var write = x.WriteCmd(Tcp.NoAck.Instance);

                var written = 0;
                while (!writer.HasMessages)
                {
                    writer.Send(x.ConnectionActor, write);
                    written += 1;
                }
                writer.ReceiveWhile(message =>
                {
                    if (message is Tcp.CommandFailed)
                        written -= 1;
                    return message;
                }, TimeSpan.FromSeconds(1));
                writer.HasMessages.ShouldBeFalse();

                writer.Send(x.ConnectionActor, write);
                writer.ExpectMsg<Tcp.CommandFailed>();
                writer.Send(x.ConnectionActor, Tcp.Write.Empty);
                writer.ExpectMsg<Tcp.CommandFailed>();

                writer.Send(x.ConnectionActor, Tcp.ResumeWriting.Instance);
                writer.ExpectNoMsg(TimeSpan.FromSeconds(1));

                while (!writer.HasMessages) 
                    x.PullFromServerSide(x.TestSize);
                writer.ExpectMsg(Tcp.WritingResumed.Instance, TimeSpan.Zero);

                writer.Send(x.ConnectionActor, x.WriteCmd(Works.Instance));
                writer.ExpectMsg(Works.Instance);
            });
        }

        [Fact]
        public void An_Outgoing_Connection_Must_support_ResumeWriting_queue_flushed()
        {
            new EstablishedConnectionTest(this).Run(x =>
            {
                var writer = CreateTestProbe();
                var write = x.WriteCmd(Tcp.NoAck.Instance);

                var written = 0;
                while (!writer.HasMessages)
                {
                    writer.Send(x.ConnectionActor, write);
                    written += 1;
                }
                writer.ReceiveWhile(message =>
                {
                    if (message is Tcp.CommandFailed)
                        written -= 1;
                    return message;
                }, TimeSpan.FromSeconds(1));
             
                x.PullFromServerSide(x.TestSize * written);

                writer.Send(x.ConnectionActor, write);
                writer.ExpectMsg<Tcp.CommandFailed>();
                writer.Send(x.ConnectionActor, Tcp.Write.Empty);
                writer.ExpectMsg<Tcp.CommandFailed>();

                writer.Send(x.ConnectionActor, Tcp.ResumeWriting.Instance);
                writer.ExpectMsg(Tcp.WritingResumed.Instance, TimeSpan.FromSeconds(1));

                writer.Send(x.ConnectionActor, x.WriteCmd(Works.Instance));
                writer.ExpectMsg(Works.Instance);
            });
        }

        [Fact]
        public void An_Outgoing_Connection_Must_support_useResumeWriting_false_backed_up()
        {
            new EstablishedConnectionTest(this, useResumeWriting: false).Run(x =>
            {
                var writer = CreateTestProbe();
                var write = x.WriteCmd(Tcp.NoAck.Instance);

                var written = 0;
                while (!writer.HasMessages)
                {
                    writer.Send(x.ConnectionActor, write);
                    written += 1;
                }
                writer.ReceiveWhile(message =>
                {
                    if (message is Tcp.CommandFailed)
                        written -= 1;
                    return message;
                }, TimeSpan.FromSeconds(1));
                writer.HasMessages.ShouldBeFalse();

                writer.Send(x.ConnectionActor, write);
                writer.ExpectMsg<Tcp.CommandFailed>();
                writer.Send(x.ConnectionActor, Tcp.Write.Empty);
                writer.ExpectMsg<Tcp.CommandFailed>();

                x.PullFromServerSide(x.TestSize * written);

                writer.Send(x.ConnectionActor, x.WriteCmd(Works.Instance));
                writer.ExpectMsg(Works.Instance);
            });
        }

        [Fact]
        public void An_Outgoing_Connection_Must_support_useResumeWriting_false_queue_flushed()
        {
            new EstablishedConnectionTest(this, useResumeWriting: false).Run(x =>
            {
                var writer = CreateTestProbe();
                var write = x.WriteCmd(Tcp.NoAck.Instance);

                var written = 0;
                while (!writer.HasMessages)
                {
                    writer.Send(x.ConnectionActor, write);
                    written += 1;
                }
                writer.ReceiveWhile(message =>
                {
                    if (message is Tcp.CommandFailed)
                        written -= 1;
                    return message;
                }, TimeSpan.FromSeconds(1));

                x.PullFromServerSide(x.TestSize * written);

                writer.Send(x.ConnectionActor, x.WriteCmd(Works.Instance));
                writer.ExpectMsg(Works.Instance);
            });
        }

        [Fact]
        public void An_Outgoing_Connection_Must_report_abort_before_handler_is_registered()
        {
            var bindAddress = new IPEndPoint(IPAddress.Loopback, 23402);
            var serverSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            serverSocket.Bind(bindAddress);
            serverSocket.Listen(100);

            var connectionProbe = CreateTestProbe();
            connectionProbe.Send(Sys.Tcp(), new Tcp.Connect(bindAddress));

            Sys.Tcp().Tell(new Tcp.Connect(bindAddress));
            ExpectMsg<Tcp.Connected>();     //TODO: Investigate why this is not reuired in JVM Akka

            var socket = serverSocket.Accept();
            connectionProbe.ExpectMsg<Tcp.Connected>();
            var connectionActor = connectionProbe.Sender;
            connectionActor.Tell(PoisonPill.Instance);
            Watch(connectionActor);
            ExpectTerminated(connectionActor);

            Assert.Throws<SocketException>(() => socket.Receive(new byte[200]));
        }
    }

    class LocalServerTest : IChannelRegistry
    {
        //protected readonly SocketAsyncEventArgsPool pool = new SocketAsyncEventArgsPool(100, SocketChannel.Select);
        private readonly ActorSystem _system;

        protected IPEndPoint ServerAddress = TestUtils.TemporaryServerAddress();
        internal SocketChannel LocalServerChannel = SocketChannel.Open();
        internal TestProbe UserHandler { get; private set; }
        internal TestProbe Selector { get; private set; }

        public TestProbe RegisterCallReceiver { get; private set; }
        public TestProbe InterestCallReceiver { get; private set; }

        protected readonly TestProbe ChannelProbe;

        public LocalServerTest(TestKitBase kit)
        {
            _system = kit.Sys;

            UserHandler = kit.CreateTestProbe();
            Selector = kit.CreateTestProbe();
            RegisterCallReceiver = kit.CreateTestProbe();
            InterestCallReceiver = kit.CreateTestProbe();

            ChannelProbe = kit.CreateTestProbe();
        }

        

        public void Run(Action<LocalServerTest> body)
        {
            try
            {
                SetServerSocketOptions();
                LocalServerChannel.Socket.Bind(ServerAddress);
                LocalServerChannel.Socket.Blocking = false;
                LocalServerChannel.Socket.Listen(100);
                body(this);
            }
            finally
            {
                LocalServerChannel.Close();
            }
            
        }

        public void Register(SocketChannel channel, SocketAsyncOperation? initialOps, IActorRef channelActor)
        {
            
            channel.Register(channelActor, initialOps);

            RegisterCallReceiver.Ref.Tell(new TcpConnectionSpec.Registration(channel, initialOps));
        }
       
        public void SetServerSocketOptions() { } 


        public TestActorRef<TcpOutgoingConnection> CreateConnectionActor(params Inet.SocketOption[] options)
        {
            return CreateConnectionActor(ServerAddress, options);
        }

        public TestActorRef<TcpOutgoingConnection> CreateConnectionActor(IPEndPoint serverAddress, IEnumerable<Inet.SocketOption> options = null, TimeSpan? timeout =null, bool pullMode = false)
        {
            var @ref = CreateConnectionActorWithoutRegistration(serverAddress, options, timeout: timeout, pullMode: pullMode);
            @ref.Tell(NewChannelRegistration());
            return @ref;
        }

        
        public ChannelRegistration NewChannelRegistration()
        {
            return new ChannelRegistration(
                enableInterest: op => InterestCallReceiver.Ref.Tell((int) op),
                disableInterest: op => InterestCallReceiver.Ref.Tell(-(int) op)
                );
        }

        class TestTcpOutgoingConnection : TcpOutgoingConnection
        {
            public TestTcpOutgoingConnection(TcpExt tcp, IChannelRegistry channelRegistry, IActorRef commander, Tcp.Connect connect)
                : base(tcp, channelRegistry, commander, connect) 
            { }
            protected override void PostRestart(Exception reason)
            {
                Context.Stop(Self);
            }
        }
        public TestActorRef<TcpOutgoingConnection> CreateConnectionActorWithoutRegistration(EndPoint serverAddress, IEnumerable<Inet.SocketOption> options = null, TimeSpan? timeout = null, bool pullMode = false)
        {
            var tcp = Tcp.Instance.Apply(_system);

            return new TestActorRef<TcpOutgoingConnection>(_system, Props.Create(() =>
                        new TestTcpOutgoingConnection(tcp, this, UserHandler.Ref, new Tcp.Connect(serverAddress, null, options, timeout, pullMode))));
        }
    }

    class UnacceptedConnectionTest : LocalServerTest
    {
        private readonly bool _pullMode;

        private TestActorRef<TcpOutgoingConnection> _connectionActor;
        private SocketChannel _clientSideChannel;

        public UnacceptedConnectionTest(TestKitBase kit, bool pullMode = false)
            : base(kit)
        {
            _pullMode = pullMode;
        }

        internal TestActorRef<TcpOutgoingConnection> ConnectionActor
        {
            get {
                return _connectionActor ??
                       (_connectionActor = CreateConnectionActor(ServerAddress, pullMode: _pullMode));
            }
            set { _connectionActor = value; }
        }

        internal SocketChannel ClientSideChannel
        {
            get { return _clientSideChannel ?? (_clientSideChannel = ConnectionActor.UnderlyingActor.Channel); }
        }

        public void Run(Action<UnacceptedConnectionTest> body)
        {
            base.Run(x =>
            {
                RegisterCallReceiver.ExpectMsgFrom<TcpConnectionSpec.Registration>(ConnectionActor.Ref, message =>
                    message.Channel == ClientSideChannel && 
                    message.InitialOps.HasValue && 
                    message.InitialOps.Value == SocketAsyncOperation.None);
                
                body(this);
            });
        }

        internal SocketChannel AcceptServerSideChannel(SocketChannel localServer)
        {
            var promise = new TaskCompletionSource<Socket>();
            var task = promise.Task;

            localServer.Socket.Listen(100);
            localServer.Socket.BeginAccept(ar => promise.SetResult(localServer.Socket.EndAccept(ar)), null);

            Selector.Send(ConnectionActor, SelectionHandler.ChannelConnectable.Instance);

            task.Wait();

            var channel = new SocketChannel(task.Result);
            channel.Register(ChannelProbe, SocketAsyncOperation.None);
            return channel;
        }

    }

    class EstablishedConnectionTest : UnacceptedConnectionTest
    {
        private readonly TestKitBase _kit;

        private SocketChannel _serverSideChannel;
        private TestProbe _connectionHandler;

        public EstablishedConnectionTest(TestKitBase kit, bool keepOpenOnPeerClosed = false, bool useResumeWriting = true, bool pullMode = false) 
            : base(kit, pullMode)
        {
            _kit = kit;
            KeepOpenOnPeerClosed = keepOpenOnPeerClosed;
            UseResumeWriting = useResumeWriting;
            PullMode = pullMode;
        }

        public SocketChannel ServerSideChannel
        {
            get
            {
                if (_serverSideChannel == null)
                    _serverSideChannel = AcceptServerSideChannel(LocalServerChannel);
                return _serverSideChannel;
            }
        }
        public TestProbe ConnectionHandler
        {
            get { return _connectionHandler ?? (_connectionHandler = _kit.CreateTestProbe()); }
        }
        

        public void Run(Action<EstablishedConnectionTest> body)
        {
            Run((UnacceptedConnectionTest x) =>
            {
                try
                {
                    ServerSideChannel.Socket.Blocking = false;

                    // JVM Akka always excpect CONNECT, which seems incorrect
                    // We will not receive a CONNECT if Socket.BeginConnect completed synchronously
                    // We therfore just igenore the CONNECT if it is in the queue
                    //What JVM Akka does:  InterestCallReceiver.ExpectMsg((int)SocketAsyncOperation.Connect); 
                    if (InterestCallReceiver.ReceiveWhile<object>(m => m is int && (int)m == (int)SocketAsyncOperation.Connect, TimeSpan.Zero, TimeSpan.Zero, 1).Any())
                        Selector.Send(ConnectionActor, SelectionHandler.ChannelConnectable.Instance);   // Only send ChannelConnectable if we did not complete synchronously 

                    UserHandler.ExpectMsg<Tcp.Connected>(message => ((IPEndPoint) message.RemoteAddress).Port.ShouldBe(ServerAddress.Port)); //TODO: compare full endpoint, not only port
 
                    UserHandler.Send(ConnectionActor,
                        new Tcp.Register(ConnectionHandler.Ref, KeepOpenOnPeerClosed, UseResumeWriting));
                    if (!PullMode)
                        InterestCallReceiver.ExpectMsg((int)SocketAsyncOperation.Receive);

                    body(this);
                }
                finally 
                {
                    ServerSideChannel.Close();
                }
            });

        }

        public readonly int TestSize = 10000;

        public Tcp.Write WriteCmd(Tcp.Event ack)
        {
            return Tcp.Write.Create(ByteString.Create(new byte[TestSize]), ack);
        }

        public void CloseServerSideAndWaitForClientReadable(bool fullClose = true)
        {
            if (fullClose) ServerSideChannel.Close();
            else ServerSideChannel.Socket.Shutdown(SocketShutdown.Send);
            Thread.Sleep(200);
        }

        public bool KeepOpenOnPeerClosed { get; set; }
        public bool UseResumeWriting { get; set; }
        public bool PullMode { get; set; }


        public void PullFromServerSide(int remaining)
        {
            PullFromServerSide(remaining, 1000, new ByteBuffer(new byte[remaining]));
        }

        public void PullFromServerSide(int remaining, int remainingRetries, ByteBuffer into)
        {
            if (remainingRetries <= 0)
                throw new AssertionFailedException("Pulling took too many loops,  remaining data: " + remaining);
            if (remaining > 0)
            {
                if (InterestCallReceiver.HasMessages)
                {
                    InterestCallReceiver.ExpectMsg((int)SocketAsyncOperation.Send);

                }

                Selector.Send(ConnectionActor, SelectionHandler.ChannelWritable.Instance);

                var read = ServerSideChannel.Read(into);
                if (read == -1) throw new IllegalStateException("Connection was closed unexpectedly with remaining bytes " + remaining);
                if (read == 0) throw new IllegalStateException("Made no progress");

                PullFromServerSide(remaining - read, remainingRetries - 1, into);
            }
        }

        public void ExpectReceivedString(string data)
        {
            Selector.Send(ConnectionActor, SelectionHandler.ChannelReadable.Instance);

            var gotReceived = ConnectionHandler.ExpectMsg<Tcp.Received>();
            var receivedString = gotReceived.Data.DecodeString(Encoding.ASCII);
            data.ShouldStartWith(receivedString);
            if (receivedString.Length < data.Length)
                ExpectReceivedString(new string(data.Drop(receivedString.Length).ToArray()));
        }

        public void AssertThisConnectionActorTerminated()
        {
            //TODO: Impliment
        }

        public void AbortClose(SocketChannel channel)
        {
            // TODO: Do we need to handle expection like JVM?

            channel.Socket.LingerState = new LingerOption(true, 0);
            channel.Close();
        }
    }


    public static class TestUtils
    {
        public static IPEndPoint TemporaryServerAddress(string hostName = "127.0.0.1", bool udp = false)
        {
            var host = new IPEndPoint(IPAddress.Parse(hostName), 0);
            using (var socket = new Socket(
                udp ? SocketType.Dgram : SocketType.Stream,
                udp ? ProtocolType.Udp : ProtocolType.Tcp))
            {
                socket.Bind(host);
                return new IPEndPoint(IPAddress.Loopback, ((IPEndPoint) socket.LocalEndPoint).Port);
            }
        }
        public static IEnumerable<IPEndPoint> TemporaryServerAddresses(int numberOfAddresses, string hostName = "127.0.0.1", bool udp = false)
        {
            return Enumerable.Range(0, numberOfAddresses).Select(i => TemporaryServerAddress(hostName, udp));
        }

    }

    
}
