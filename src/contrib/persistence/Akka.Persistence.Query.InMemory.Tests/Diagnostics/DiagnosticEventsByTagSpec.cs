// -----------------------------------------------------------------------
//  <copyright file="DiagnosticEventsByTagSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Streams;
using Akka.Streams.TestKit;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static Akka.Persistence.Query.Offset;

namespace Akka.Persistence.Query.InMemory.Tests.Diagnostics;

/// <summary>
/// Diagnostic version of EventsByTagSpec that captures detailed timing information.
/// Run this test to gather diagnostic data about the race condition.
/// </summary>
public class DiagnosticEventsByTagSpec : Akka.TestKit.Xunit2.TestKit, IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ActorMaterializer _materializer;
    private readonly IReadJournal _readJournal;
    private readonly ThreadPoolMonitor _threadPoolMonitor;

    private static Config Config()
    {
        return ConfigurationFactory.ParseString(@"
            akka.loglevel = DEBUG
            akka.persistence.journal.inmem {
                event-adapters {
                    color-tagger = ""Akka.Persistence.Query.InMemory.Tests.Diagnostics.DiagnosticColorFruitTagger, Akka.Persistence.Query.InMemory.Tests""
                }
                event-adapter-bindings = {
                    ""System.String"" = color-tagger
                }
            }")
            .WithFallback(InMemoryPersistenceSpecConfig.Config);
    }

    public DiagnosticEventsByTagSpec(ITestOutputHelper output)
        : base(Config(), nameof(DiagnosticEventsByTagSpec), output)
    {
        _output = output;

        // Start diagnostic timeline
        DiagnosticTimeline.Start();
        DiagnosticTimeline.Record("Test", "Constructor started");

        // Start thread pool monitoring
        _threadPoolMonitor = new ThreadPoolMonitor(intervalMs: 100, durationMs: 15000);

        // Record before loading Persistence extension
        DiagnosticTimeline.Record("Test", "About to load Persistence extension");

        // IMPORTANT: This is the fix - force-load Persistence extension
        // COMMENT THIS OUT to reproduce the race condition
        Persistence.Instance.Apply(Sys);

        DiagnosticTimeline.Record("Test", "Persistence extension loaded");

        // Record before loading ReadJournal
        DiagnosticTimeline.Record("Test", "About to load ReadJournal");

        _materializer = Sys.Materializer();
        _readJournal = Sys.ReadJournalFor<InMemoryReadJournal>(InMemoryReadJournal.Identifier);

        DiagnosticTimeline.Record("Test", "Constructor completed");
    }

    public new void Dispose()
    {
        DiagnosticTimeline.Record("Test", "Dispose started");
        _threadPoolMonitor.Dispose();
        DiagnosticTimeline.StopAndDump(_output);
        base.Dispose();
    }

    /// <summary>
    /// This is the test that was timing out. Run it with diagnostics to see what happens.
    /// </summary>
    [Fact]
    public void Diagnostic_ReadJournal_live_query_EventsByTag_should_find_events_from_offset_exclusive()
    {
        DiagnosticTimeline.Record("Test", "Test method started");

        if (_readJournal is not IEventsByTagQuery queries)
            throw new InvalidOperationException("ReadJournal does not implement IEventsByTagQuery");

        // Create actors - this is where the race condition can occur
        DiagnosticTimeline.Record("Test", "Creating actor a");
        var a = Sys.ActorOf(DiagnosticTestActor.Props("a"), "a");
        DiagnosticTimeline.Record("Test", "Actor a created, ActorRef obtained");

        DiagnosticTimeline.Record("Test", "Creating actor b");
        var b = Sys.ActorOf(DiagnosticTestActor.Props("b"), "b");
        DiagnosticTimeline.Record("Test", "Actor b created, ActorRef obtained");

        DiagnosticTimeline.Record("Test", "Creating actor c");
        var c = Sys.ActorOf(DiagnosticTestActor.Props("c"), "c");
        DiagnosticTimeline.Record("Test", "Actor c created, ActorRef obtained");

        // Send messages and wait for responses
        DiagnosticTimeline.Record("Test", "Sending 'hello' to actor a");
        a.Tell("hello");

        DiagnosticTimeline.Record("Test", "Waiting for 'hello-done' from actor a");
        var helloResult = ExpectMsg<string>(TimeSpan.FromSeconds(10));
        DiagnosticTimeline.Record("Test", $"Received: {helloResult}");
        helloResult.Should().Be("hello-done");

        DiagnosticTimeline.Record("Test", "Sending 'a green apple' to actor a");
        a.Tell("a green apple");
        ExpectMsg("a green apple-done", TimeSpan.FromSeconds(10));
        DiagnosticTimeline.Record("Test", "Received 'a green apple-done'");

        DiagnosticTimeline.Record("Test", "Sending 'a black car' to actor b");
        b.Tell("a black car");
        ExpectMsg("a black car-done", TimeSpan.FromSeconds(10));
        DiagnosticTimeline.Record("Test", "Received 'a black car-done'");

        DiagnosticTimeline.Record("Test", "Sending 'something else' to actor a");
        a.Tell("something else");
        ExpectMsg("something else-done", TimeSpan.FromSeconds(10));
        DiagnosticTimeline.Record("Test", "Received 'something else-done'");

        DiagnosticTimeline.Record("Test", "Sending 'a green banana' to actor a");
        a.Tell("a green banana");
        ExpectMsg("a green banana-done", TimeSpan.FromSeconds(10));
        DiagnosticTimeline.Record("Test", "Received 'a green banana-done'");

        DiagnosticTimeline.Record("Test", "Sending 'a green leaf' to actor b");
        b.Tell("a green leaf");
        ExpectMsg("a green leaf-done", TimeSpan.FromSeconds(10));
        DiagnosticTimeline.Record("Test", "Received 'a green leaf-done'");

        DiagnosticTimeline.Record("Test", "Sending 'a green cucumber' to actor c");
        c.Tell("a green cucumber");
        ExpectMsg("a green cucumber-done", TimeSpan.FromSeconds(10));
        DiagnosticTimeline.Record("Test", "Received 'a green cucumber-done'");

        // Query for green events
        DiagnosticTimeline.Record("Test", "Creating EventsByTag query for 'green'");
        var greenSrc1 = queries.EventsByTag("green", offset: NoOffset());
        var probe1 = greenSrc1.RunWith(this.SinkProbe<EventEnvelope>(), _materializer);

        DiagnosticTimeline.Record("Test", "Requesting 2 events from probe1");
        probe1.Request(2);

        DiagnosticTimeline.Record("Test", "Expecting first green event");
        var env1 = probe1.ExpectNext<EventEnvelope>(_ => true);
        DiagnosticTimeline.Record("Test", $"Got event: {env1.Event} from {env1.PersistenceId}");
        env1.PersistenceId.Should().Be("a");
        env1.Event.Should().Be("a green apple");

        DiagnosticTimeline.Record("Test", "Expecting second green event");
        var env2 = probe1.ExpectNext<EventEnvelope>(_ => true);
        DiagnosticTimeline.Record("Test", $"Got event: {env2.Event} from {env2.PersistenceId}, offset: {env2.Offset}");
        env2.PersistenceId.Should().Be("a");
        env2.Event.Should().Be("a green banana");

        var offs = env2.Offset;

        DiagnosticTimeline.Record("Test", "Cancelling probe1");
        probe1.Cancel();

        // Query from offset
        DiagnosticTimeline.Record("Test", $"Creating EventsByTag query from offset {offs}");
        var greenSrc = queries.EventsByTag("green", offset: offs);
        var probe2 = greenSrc.RunWith(this.SinkProbe<EventEnvelope>(), _materializer);

        DiagnosticTimeline.Record("Test", "Requesting 10 events from probe2");
        probe2.Request(10);

        DiagnosticTimeline.Record("Test", "Expecting 'a green leaf' event");
        var env3 = probe2.ExpectNext<EventEnvelope>(_ => true);
        DiagnosticTimeline.Record("Test", $"Got event: {env3.Event} from {env3.PersistenceId}");
        env3.PersistenceId.Should().Be("b");
        env3.Event.Should().Be("a green leaf");

        DiagnosticTimeline.Record("Test", "Cancelling probe2");
        probe2.Cancel();

        DiagnosticTimeline.Record("Test", "Test completed successfully!");
    }

    /// <summary>
    /// Run this test WITHOUT the Persistence.Instance.Apply(Sys) fix to reproduce the issue.
    /// </summary>
    [Fact(Skip = "Enable this test to reproduce the race condition - will likely timeout")]
    public void Diagnostic_WITHOUT_FIX_should_timeout()
    {
        // This test is identical to the above but the fix is commented out in a separate spec
        // To reproduce: create a copy of this class without the Persistence.Instance.Apply(Sys) call
    }
}
