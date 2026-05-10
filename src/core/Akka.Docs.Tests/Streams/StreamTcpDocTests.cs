//-----------------------------------------------------------------------
// <copyright file="StreamTcpDocTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit;
using Xunit;
using Akka.Actor;
using Akka.Util;
using Tcp = Akka.Streams.Dsl.Tcp;
using Akka.Configuration;

namespace DocsExamples.Streams
{
    public class StreamTcpDocTests : TestKit
    {
        private ActorMaterializer Materializer { get; }

        public StreamTcpDocTests(ITestOutputHelper output)
            : base("{}", "Actorname", output)
        {
            Materializer = Sys.Materializer();
        }

        [Fact]
        public async Task Simple_server_connection_must_bind_and_unbind()
        {
            #region echo-server-simple-bind
            // define an incoming request processing logic
            Flow<ReadOnlySequence<byte>, ReadOnlySequence<byte>, NotUsed> echo = Flow.Create<ReadOnlySequence<byte>>();

            Tcp.ServerBinding binding = await Sys.TcpStream()
                .BindAndHandle(echo, Materializer, "localhost", 9000);

            Console.WriteLine($"Server listening at {binding.LocalAddress}");

            // close server after everything is done
            await binding.Unbind();
            #endregion
        }

        [Fact]
        public void Simple_server_connection_must_handle_connection()
        {
            #region echo-server-simple-handle
            Source<Tcp.IncomingConnection, Task<Tcp.ServerBinding>> connections =
                Sys.TcpStream().Bind("127.0.0.1", 8888);

            connections.RunForeach(connection =>
            {
                Console.WriteLine($"New connection from: {connection.RemoteAddress}");

                var echo = Flow.Create<ReadOnlySequence<byte>>()
                    .Via(Framing.Delimiter(
                        Encoding.ASCII.GetBytes("\n").AsMemory(),
                        maximumFrameLength: 256,
                        allowTruncation: true))
                    .Select(c => Encoding.ASCII.GetString(c.ToArray()))
                    .Select(c => c + "!!!\n")
                    .Select(s => new ReadOnlySequence<byte>(Encoding.ASCII.GetBytes(s).AsMemory()));

                connection.HandleWith(echo, Materializer);
            }, Materializer);
            #endregion
        }

        [Fact]
        public void Simple_server_connection_must_close_incoming_connection()
        {
            Source<Tcp.IncomingConnection, Task<Tcp.ServerBinding>> connections =
                Sys.TcpStream().Bind("127.0.0.1", 8888);

            connections.RunForeach(connection =>
            {
                #region close-incoming-connection
                var closed = Flow.FromSinkAndSource(Sink.Cancelled<ReadOnlySequence<byte>>(), Source.Empty<ReadOnlySequence<byte>>());
                connection.HandleWith(closed, Materializer);
                #endregion
            }, Materializer);
        }

        [Fact]
        public async Task Simple_server_must_initial_server_banner_echo_server()
        {
            var serverProbe = CreateTestProbe();

            #region welcome-banner-chat-server
            // Use ToMaterialized to capture the binding task so we can await it
            var (bindingTask, _) = Sys.TcpStream().Bind("127.0.0.1", 0) // Use port 0 for dynamic port assignment
                .ToMaterialized(Sink.ForEach<Tcp.IncomingConnection>(connection =>
                {
                    // server logic, parses incoming commands
                    var commandParser = Flow.Create<string>().TakeWhile(c => c != "BYE").Select(c => c + "!");

                    var welcomeMessage = $"Welcome to: {connection.LocalAddress}, you are: {connection.RemoteAddress}!";
                    var welcome = Source.Single(welcomeMessage);

                    var serverLogic = Flow.Create<ReadOnlySequence<byte>>()
                        .Via(Framing.Delimiter(
                            Encoding.ASCII.GetBytes("\n").AsMemory(),
                            maximumFrameLength: 256,
                            allowTruncation: true))
                        .Select(c => Encoding.ASCII.GetString(c.ToArray()))
                        .Select(command =>
                        {
                            serverProbe.Tell(command);
                            return command;
                        })
                        .Via(commandParser)
                        .Merge(welcome)
                        .Select(c => c + "\n")
                        .Select(s => new ReadOnlySequence<byte>(Encoding.ASCII.GetBytes(s).AsMemory()));

                    connection.HandleWith(serverLogic, Materializer);
                }), Keep.Both)
                .Run(Materializer);
            #endregion

            // Wait for server to bind before connecting client - fixes race condition
            var binding = await bindingTask;
            var serverPort = ((System.Net.IPEndPoint)binding.LocalAddress).Port;

            var input = new ConcurrentQueue<string>(new[] { "Hello world", "What a lovely day" });

            string ReadLine(string prompt) => input.TryDequeue(out var cmd) ? cmd : "q";

            try
            {
                #region repl-client
                var connection = Sys.TcpStream().OutgoingConnection("127.0.0.1", serverPort);

                var replParser = Flow.Create<string>().TakeWhile(c => c != "q")
                    .Concat(Source.Single("BYE"))
                    .Select(elem => new ReadOnlySequence<byte>(Encoding.ASCII.GetBytes($"{elem}\n").AsMemory()));

                var repl = Flow.Create<ReadOnlySequence<byte>>()
                    .Via(Framing.Delimiter(
                        Encoding.ASCII.GetBytes("\n").AsMemory(),
                        maximumFrameLength: 256,
                        allowTruncation: true))
                    .Select(c => Encoding.ASCII.GetString(c.ToArray()))
                    .Select(text =>
                    {
                        Output.WriteLine($"Server: {text}");
                        return text;
                    })
                    .Select(_ => ReadLine("> "))
                    .Via(replParser);

                // Client stream runs in background - completion is not awaited
                // since we verify behavior via serverProbe
                _ = connection.Join(repl).Run(Materializer);
                #endregion

                await serverProbe.ExpectMsgAsync<string>(s => s == "Hello world", TimeSpan.FromSeconds(20));
                await serverProbe.ExpectMsgAsync<string>(s => s == "What a lovely day");
                await serverProbe.ExpectMsgAsync<string>(s => s == "BYE");
            }
            finally
            {
                await binding.Unbind();
            }
        }
    }
}
