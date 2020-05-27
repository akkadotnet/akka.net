//-----------------------------------------------------------------------
// <copyright file="StreamRefsDocTests.cs" company="Akka.NET Project">
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
using System.Linq;

namespace DocsExamples.Streams
{
    public class StreamRefsDocTests : TestKit
    {
        #region data-source-actor
        public sealed class RequestLogs
        {
            public int StreamId { get; }

            public RequestLogs(int streamId)
            {
                StreamId = streamId;
            }
        }

        public sealed class LogsOffer
        {
            public int StreamId { get; }
            public ISourceRef<string> SourceRef { get; }

            public LogsOffer(int streamId, ISourceRef<string> sourceRef)
            {
                StreamId = streamId;
                SourceRef = sourceRef;
            }
        }

        public class DataSource : ReceiveActor
        {
            public DataSource()
            {
                Receive<RequestLogs>(request =>
                {
                    // create a source
                    StreamLogs(request.StreamId)
                        // materialize it using stream refs
                        .RunWith(StreamRefs.SourceRef<string>(), Context.System.Materializer())
                        // and send to sender
                        .PipeTo(Sender, success: sourceRef => new LogsOffer(request.StreamId, sourceRef));
                });
            }

            private Source<string, NotUsed> StreamLogs(int streamId) =>
                Source.From(Enumerable.Range(1, 100)).Select(i => i.ToString());
        }
        #endregion

        #region data-sink-actor
        public sealed class PrepareUpload
        {
            public string Id { get; }
            public PrepareUpload(string id)
            {
                Id = id;
            }
        }

        public sealed class MeasurementsSinkReady
        {
            public string Id { get; }
            public ISinkRef<string> SinkRef { get; }
            public MeasurementsSinkReady(string id, ISinkRef<string> sinkRef)
            {
                Id = id;
                SinkRef = sinkRef;
            }
        }

        class DataReceiver : ReceiveActor
        {
            public DataReceiver()
            {
                Receive<PrepareUpload>(prepare =>
                {
                    // obtain a source you want to offer
                    var sink = LogsSinksFor(prepare.Id);

                    // materialize sink ref (remote is source data for us)
                    StreamRefs.SinkRef<string>()
                        .To(sink)
                        .Run(Context.System.Materializer())
                        .PipeTo(Sender, success: sinkRef => sinkRef);
                });
            }

            private Sink<string, Task> LogsSinksFor(string id) =>
                Sink.ForEach<string>(Console.WriteLine);
        }
        #endregion

        private ActorMaterializer Materializer { get; }

        public StreamRefsDocTests(ITestOutputHelper output)
            : base("{}", output)
        {
            Materializer = Sys.Materializer();
        }

        [Fact]
        public async Task SourceRef_must_propagate_source_from_another_system()
        {
            #region source-ref-materialization
            var sourceActor = Sys.ActorOf(Props.Create<DataSource>(), "dataSource");

            var offer = await sourceActor.Ask<LogsOffer>(new RequestLogs(1337));
            await offer.SourceRef.Source.RunForeach(Console.WriteLine, Materializer);
            #endregion
        }

        [Fact]
        public async Task SinkRef_must_receive_messages_from_another_system()
        {
            #region sink-ref-materialization
            var receiver = Sys.ActorOf(Props.Create<DataReceiver>(), "receiver");

            var ready = await receiver.Ask<ISinkRef<string>>(new PrepareUpload("id"), timeout: TimeSpan.FromSeconds(30));

            // stream local metrics to Sink's origin:
            Source.From(Enumerable.Range(1, 100))
                .Select(i => i.ToString())
                .RunWith(ready.Sink, Materializer);
            #endregion
        }
    }
}
