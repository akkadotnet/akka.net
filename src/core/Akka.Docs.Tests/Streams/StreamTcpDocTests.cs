//-----------------------------------------------------------------------
// <copyright file="StreamTcpDocTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit2;
using Xunit;
using Xunit.Abstractions;
using Akka.Actor;
using Akka.IO;
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
            Flow<ByteString, ByteString, NotUsed> echo = Flow.Create<ByteString>();

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

                var echo = Flow.Create<ByteString>()
                    .Via(Framing.Delimiter(
                        ByteString.FromString("\n"),
                        maximumFrameLength: 256,
                        allowTruncation: true))
                    .Select(c => c.ToString())
                    .Select(c => c + "!!!\n")
                    .Select(ByteString.FromString);

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
                var closed = Flow.FromSinkAndSource(Sink.Cancelled<ByteString>(), Source.Empty<ByteString>());
                connection.HandleWith(closed, Materializer);
                #endregion
            }, Materializer);
        }

        [Fact]
        public void Simple_server_must_initial_server_banner_echo_server()
        {
            var connections = Sys.TcpStream().Bind("127.0.0.1", 8888);
            var serverProbe = CreateTestProbe();

            #region welcome-banner-chat-server
            connections.RunForeach(connection =>
            {
                // server logic, parses incoming commands
                var commandParser = Flow.Create<string>().TakeWhile(c => c != "BYE").Select(c => c + "!");

                var welcomeMessage = $"Welcome to: {connection.LocalAddress}, you are: {connection.RemoteAddress}!";
                var welcome = Source.Single(welcomeMessage);

                var serverLogic = Flow.Create<ByteString>()
                    .Via(Framing.Delimiter(
                        ByteString.FromString("\n"),
                        maximumFrameLength: 256,
                        allowTruncation: true))
                    .Select(c => c.ToString())
                    .Select(command =>
                    {
                        serverProbe.Tell(command);
                        return command;
                    })
                    .Via(commandParser)
                    .Merge(welcome)
                    .Select(c => c + "\n")
                    .Select(ByteString.FromString);

                connection.HandleWith(serverLogic, Materializer);
            }, Materializer);
            #endregion

            var input = new ConcurrentQueue<string>(new[] { "Hello world", "What a lovely day" });

            string ReadLine(string prompt) => input.TryDequeue(out var cmd) ? cmd : "q";

            {
                var connection = Sys.TcpStream().OutgoingConnection("127.0.0.1", 8888);
            }

            {
                #region repl-client
                var connection = Sys.TcpStream().OutgoingConnection("127.0.0.1", 8888);

                var replParser = Flow.Create<string>().TakeWhile(c => c != "q")
                    .Concat(Source.Single("BYE"))
                    .Select(elem => ByteString.FromString($"{elem}\n"));

                var repl = Flow.Create<ByteString>()
                    .Via(Framing.Delimiter(
                        ByteString.FromString("\n"),
                        maximumFrameLength: 256,
                        allowTruncation: true))
                    .Select(c => c.ToString())
                    .Select(text =>
                    {
                        Output.WriteLine($"Server: {text}");
                        return text;
                    })
                    .Select(text => ReadLine("> "))
                    .Via(replParser);

                connection.Join(repl).Run(Materializer);
                #endregion
            }

            serverProbe.ExpectMsg("Hello world", TimeSpan.FromSeconds(20));
            serverProbe.ExpectMsg("What a lovely day");
            serverProbe.ExpectMsg("BYE");
        }
    }
}
