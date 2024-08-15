// -----------------------------------------------------------------------
//  <copyright file="StreamRefsDocTests.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit2;
using Xunit;
using Xunit.Abstractions;

namespace DocsExamples.Streams;

public class StreamRefsDocTests : TestKit
{
    public StreamRefsDocTests(ITestOutputHelper output)
        : base("{}", output)
    {
        Materializer = Sys.Materializer();
    }

    private ActorMaterializer Materializer { get; }

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

        var ready = await receiver.Ask<MeasurementsSinkReady>(new PrepareUpload("id"), TimeSpan.FromSeconds(30));

        // stream local metrics to Sink's origin:
        Source.From(Enumerable.Range(1, 100))
            .Select(i => i.ToString())
            .RunWith(ready.SinkRef.Sink, Materializer);

        #endregion
    }

    #region data-source-actor

    public sealed class RequestLogs
    {
        public RequestLogs(int streamId)
        {
            StreamId = streamId;
        }

        public int StreamId { get; }
    }

    public sealed class LogsOffer
    {
        public LogsOffer(int streamId, ISourceRef<string> sourceRef)
        {
            StreamId = streamId;
            SourceRef = sourceRef;
        }

        public int StreamId { get; }
        public ISourceRef<string> SourceRef { get; }
    }

    public class DataSource : ReceiveActor
    {
        public DataSource()
        {
            Receive<RequestLogs>(request =>
            {
                var sender = Sender;
                // create a source
                StreamLogs(request.StreamId)
                    // materialize it using stream refs
                    .RunWith(StreamRefs.SourceRef<string>(), Context.System.Materializer())
                    // and send to sender
                    .PipeTo(sender, success: sourceRef => new LogsOffer(request.StreamId, sourceRef));
            });
        }

        private Source<string, NotUsed> StreamLogs(int streamId)
        {
            return Source.From(Enumerable.Range(1, 100)).Select(i => i.ToString());
        }
    }

    #endregion

    #region data-sink-actor

    public sealed class PrepareUpload
    {
        public PrepareUpload(string id)
        {
            Id = id;
        }

        public string Id { get; }
    }

    public sealed class MeasurementsSinkReady
    {
        public MeasurementsSinkReady(string id, ISinkRef<string> sinkRef)
        {
            Id = id;
            SinkRef = sinkRef;
        }

        public string Id { get; }
        public ISinkRef<string> SinkRef { get; }
    }

    private class DataReceiver : ReceiveActor
    {
        public DataReceiver()
        {
            Receive<PrepareUpload>(prepare =>
            {
                // obtain a source you want to offer
                var sink = LogsSinksFor(prepare.Id);
                var sender = Sender;

                // materialize sink ref (remote is source data for us)
                StreamRefs.SinkRef<string>()
                    .To(sink)
                    .Run(Context.System.Materializer())
                    .PipeTo(sender, success: sinkRef => new MeasurementsSinkReady(prepare.Id, sinkRef));
            });
        }

        private Sink<string, Task<Done>> LogsSinksFor(string id)
        {
            return Sink.ForEach<string>(Console.WriteLine);
        }
    }

    #endregion
}