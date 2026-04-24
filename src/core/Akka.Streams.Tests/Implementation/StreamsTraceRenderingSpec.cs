//-----------------------------------------------------------------------
// <copyright file="StreamsTraceRenderingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.Implementation
{
    /// <summary>
    /// Runs each of the representative stream-trace scenarios, captures the real
    /// span data emitted by the interpreter, and renders each scenario as a
    /// markdown-formatted ASCII span tree under a <c>trace-samples/</c> directory
    /// next to the spec source file. These renderings are checked into the repo
    /// as documentation artifacts — each one is reproducible by re-running the
    /// corresponding test.
    ///
    /// No synthetic data is used. Every rendered span tree is built from the
    /// <see cref="Activity"/> objects that the real <see cref="GraphInterpreter"/>
    /// emitted during the test run.
    /// </summary>
    public class StreamsTraceRenderingSpec : AkkaSpec
    {
        private readonly ActorMaterializer _materializer;

        public StreamsTraceRenderingSpec(ITestOutputHelper output) : base(output)
        {
            _materializer = Sys.Materializer();
        }

        // ------ scenario: linear chain (Source.Queue → Select → Sink.Seq) ------
        [Fact]
        public async Task Render_linear_chain_Queue_Select_Sink()
        {
            using var collector = new StreamsActivityCollector();
            using var producers = new ProducerActivityScope("ProducerTest");

            var queue = Source.Queue<int>(16, OverflowStrategy.DropNew)
                .Select(i => i * 2)
                .ToMaterialized(Sink.Seq<int>(), Keep.Left)
                .Run(_materializer);

            using (var parent = producers.Start("producer.offer"))
            {
                (await queue.OfferAsync(42)).Should().Be(QueueOfferResult.Enqueued.Instance);
            }
            queue.Complete();

            await collector.WaitForSpansAsync(atLeast: 3, timeoutSeconds: 3);

            RenderAndSave(
                scenario: "linear-chain",
                title: "Linear chain: `Source.Queue` → `Select` → `Sink.Seq`",
                description: "Single traced producer offers one element. Stage spans form a straight " +
                             "parent chain from the producer's span down through the ingress wrap, " +
                             "the Select stage, and the terminal Seq sink stage.",
                collector: collector);
        }

        // ------ scenario: SelectAsync with user span inside the async lambda ------
        [Fact]
        public async Task Render_SelectAsync_with_user_span_inside_lambda()
        {
            using var collector = new StreamsActivityCollector();
            using var producers = new ProducerActivityScope("ProducerTest");
            using var userSource = new ActivitySource("UserWork");
            using var userListener = new ActivityListener
            {
                ShouldListenTo = s => s.Name == "UserWork",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = _ => { },
                ActivityStopped = _ => { }
            };
            ActivitySource.AddActivityListener(userListener);

            var userStoppedActivities = new List<Activity>();
            using var captureUser = new ActivityListener
            {
                ShouldListenTo = s => s.Name == "UserWork",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = a => userStoppedActivities.Add(a)
            };
            ActivitySource.AddActivityListener(captureUser);

            var sink = Source.Queue<int>(16, OverflowStrategy.DropNew)
                .SelectAsync(1, async i =>
                {
                    using var userSpan = userSource.StartActivity("user.work.SqlClient.Execute", ActivityKind.Client);
                    await Task.Yield();
                    return i * 2;
                })
                .ToMaterialized(Sink.Seq<int>(), Keep.Left)
                .Run(_materializer);

            using (var parent = producers.Start("producer.offer"))
            {
                (await sink.OfferAsync(42)).Should().Be(QueueOfferResult.Enqueued.Instance);
            }
            await collector.WaitForSpansAsync(atLeast: 3, timeoutSeconds: 3);

            // Merge the user spans into the capture so the rendering shows the
            // downstream user.work span nested inside SelectAsync.
            var merged = new List<Activity>(collector.StoppedActivities);
            merged.AddRange(userStoppedActivities);

            RenderAndSave(
                scenario: "selectasync-user-span",
                title: "`SelectAsync` with a user span inside the async lambda",
                description: "Simulates the common case where user code inside a `SelectAsync` body " +
                             "(e.g. `SqlClient.ExecuteAsync`, `HttpClient.SendAsync`) creates its own " +
                             "span. The user span inherits the `SelectAsync` stage span as parent, so " +
                             "the full producer → ingress → stage → user-code chain shares one TraceId.",
                collector: null,
                spansOverride: merged);
        }

        // ------ scenario: BatchWeighted fan-in with 3 traced producers ------
        [Fact]
        public async Task Render_BatchWeighted_fan_in_3_producers()
        {
            using var collector = new StreamsActivityCollector();
            using var producers = new ProducerActivityScope("ProducerTest");

            var started = new TaskCompletionSource<bool>();
            var release = new TaskCompletionSource<bool>();

            var queue = Source.Queue<int>(64, OverflowStrategy.DropNew)
                .BatchWeighted(
                    max: 1000L,
                    costFunction: (int _) => 1L,
                    seed: (int i) => new List<int> { i },
                    aggregate: (List<int> acc, int i) => { acc.Add(i); return acc; })
                .SelectAsync(1, async batch =>
                {
                    started.TrySetResult(true);
                    await release.Task;
                    return batch;
                })
                .ToMaterialized(Sink.Ignore<List<int>>(), Keep.Left)
                .Run(_materializer);

            // Prime — pins the downstream SelectAsync busy so later offers pile up.
            using (var primer = producers.Start("producer.offer.primer"))
                (await queue.OfferAsync(0)).Should().Be(QueueOfferResult.Enqueued.Instance);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            for (int i = 1; i <= 3; i++)
            {
                using var parent = producers.Start($"producer.offer.{i}");
                (await queue.OfferAsync(i)).Should().Be(QueueOfferResult.Enqueued.Instance);
            }

            await Task.Delay(300);
            release.SetResult(true);
            queue.Complete();

            await collector.WaitForLinkedSpanAsync();

            RenderAndSave(
                scenario: "batchweighted-fan-in",
                title: "`BatchWeighted` fan-in — three concurrent producers merged into one batch",
                description: "Three independent producer traces each offer one element into a shared " +
                             "`Source.Queue`. A slow downstream `SelectAsync(1)` holds the Batch outlet " +
                             "busy so all three elements pile into the same aggregate. On flush, the " +
                             "downstream stage span carries the first producer's trace as primary parent " +
                             "and attaches `ActivityLink`s to every other contributing producer's trace. " +
                             "A trace viewer can jump from any individual producer's trace to the shared " +
                             "batched-flush span via the link.",
                collector: collector);
        }

        // ------ scenario: Merge 1-to-1 pass-through ------
        [Fact]
        public async Task Render_Merge_two_sources_pass_through()
        {
            using var collector = new StreamsActivityCollector();
            using var producers = new ProducerActivityScope("ProducerTest");

            var runnable = Source.Queue<int>(8, OverflowStrategy.DropNew)
                .MergeMaterialized(
                    Source.Queue<int>(8, OverflowStrategy.DropNew),
                    Keep.Both)
                .Select(i => i * 2)
                .ToMaterialized(Sink.Ignore<int>(), Keep.Left)
                .Run(_materializer);

            var (qA, qB) = runnable;

            using (var pA = producers.Start("producer.A"))
                (await qA.OfferAsync(1)).Should().Be(QueueOfferResult.Enqueued.Instance);
            using (var pB = producers.Start("producer.B"))
                (await qB.OfferAsync(2)).Should().Be(QueueOfferResult.Enqueued.Instance);

            qA.Complete();
            qB.Complete();

            await collector.WaitForSpansAsync(atLeast: 4, timeoutSeconds: 5);

            RenderAndSave(
                scenario: "merge-two-sources",
                title: "`Merge` — two independent producers, 1-to-1 pass-through",
                description: "Two independent `Source.Queue`s merge into a single downstream `Select`. " +
                             "Each input element flows through as a separate 1-to-1 element (no " +
                             "aggregation), so each element's downstream stage span inherits the " +
                             "originating producer's trace via `SetFanInTraceContext` — no " +
                             "`ActivityLink`s attached because every merged output has exactly one " +
                             "contributing input.",
                collector: collector);
        }

        // ------ scenario: Broadcast fan-out (2 branches) ------
        [Fact]
        public async Task Render_Broadcast_two_branches_fan_out()
        {
            using var collector = new StreamsActivityCollector();
            using var producers = new ProducerActivityScope("ProducerTest");

            var (queue, _) = Source.Queue<int>(8, OverflowStrategy.DropNew)
                .ToMaterialized(
                    Sink.FromGraph(GraphDsl.Create(
                        Sink.Ignore<int>(),
                        Sink.Ignore<int>(),
                        Keep.Both,
                        (builder, leftSink, rightSink) =>
                        {
                            var broadcast = builder.Add(new Broadcast<int>(2));
                            builder.From(broadcast.Out(0)).Via(Flow.Create<int>().Select(i => i + 10)).To(leftSink.Inlet);
                            builder.From(broadcast.Out(1)).Via(Flow.Create<int>().Select(i => i + 20)).To(rightSink.Inlet);
                            return new SinkShape<int>(broadcast.In);
                        })),
                    Keep.Both)
                .Run(_materializer);

            using (var parent = producers.Start("producer.offer"))
                (await queue.OfferAsync(1)).Should().Be(QueueOfferResult.Enqueued.Instance);
            queue.Complete();

            await collector.WaitForSpansAsync(atLeast: 5, timeoutSeconds: 5,
                predicate: snap => snap.Count(a => a.OperationName == "akka.stream.stage Select") >= 2);

            RenderAndSave(
                scenario: "broadcast-two-branches",
                title: "`Broadcast(2)` — one producer fans out to two downstream branches",
                description: "A single traced element reaches a `Broadcast(2)` and is copied to two " +
                             "independent downstream branches. Both branches' `Select` stage spans " +
                             "inherit the producer's TraceId. Fan-out requires no stage-specific " +
                             "instrumentation — the existing per-element `SlotContext` carry through " +
                             "`ProcessPush` naturally propagates the upstream context to every copy.",
                collector: collector);
        }

        // ------ scenario: untraced Source.Tick regression guard ------
        [Fact]
        public async Task Render_untraced_source_tick_no_spans()
        {
            using var collector = new StreamsActivityCollector();

            var cancel = Source.Tick(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20), "tick")
                .Select(x => x.ToUpperInvariant())
                .SelectAsync(1, async x => { await Task.Yield(); return x; })
                .ToMaterialized(Sink.Ignore<string>(), Keep.Left)
                .Run(_materializer);

            await Task.Delay(500);
            cancel.Cancel();
            await Task.Delay(100);

            RenderAndSave(
                scenario: "untraced-tick-zero-spans",
                title: "Untraced background `Source.Tick` — zero spans emitted",
                description: "A background pipeline with no traced producer (timer-driven source) " +
                             "runs for 500ms and emits zero `\"Akka.Streams\"` spans. This is the " +
                             "cardinality guarantee that makes it safe to leave the `\"Akka.Streams\"` " +
                             "ActivitySource registered in production: streams that run without an " +
                             "external traced caller (cluster-sharded pollers, timer-driven health " +
                             "checks, internal subscription pumps) stay completely invisible to tracing.",
                collector: collector);
        }

        // --------------------------------------------------------------------
        // helpers
        // --------------------------------------------------------------------

        /// <summary>
        /// Builds an ASCII span-tree rendering of the captured activities and writes
        /// it to <c>trace-samples/{scenario}.md</c> next to this spec file.
        /// </summary>
        private void RenderAndSave(
            string scenario,
            string title,
            string description,
            StreamsActivityCollector collector,
            IReadOnlyList<Activity> spansOverride = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerMemberName] string callerMemberName = "")
        {
            var spans = spansOverride ?? collector.StoppedActivities.ToArray();
            var sb = new StringBuilder();
            sb.AppendLine($"# {title}");
            sb.AppendLine();
            sb.AppendLine(description);
            sb.AppendLine();
            sb.AppendLine($"This rendering was generated by running `StreamsTraceRenderingSpec.{callerMemberName}` " +
                          "against a live `GraphInterpreter` — the span data below is what the real interpreter " +
                          "emitted, not a hand-drawn mock.");
            sb.AppendLine();
            sb.AppendLine("## Captured spans");
            sb.AppendLine();

            if (spans.Count == 0)
            {
                sb.AppendLine("_Zero spans captured. This is the expected result for this scenario._");
            }
            else
            {
                sb.AppendLine("```");
                var tree = BuildSpanTree(spans);
                RenderTree(tree, depth: 0, sb);
                sb.AppendLine("```");
                sb.AppendLine();

                // Dump the distinct trace ids referenced so a reader can see the fan-in
                // relationship between multiple producer traces.
                var distinctTraces = spans.Select(a => a.TraceId).Distinct().ToList();
                sb.AppendLine($"## Distinct trace ids: {distinctTraces.Count}");
                sb.AppendLine();
                foreach (var tid in distinctTraces)
                    sb.AppendLine($"- `{tid}`");
                sb.AppendLine();

                var linked = spans.Where(a => (a.Links?.Count() ?? 0) > 0).ToList();
                if (linked.Count > 0)
                {
                    sb.AppendLine("## Fan-in ActivityLinks");
                    sb.AppendLine();
                    foreach (var f in linked)
                    {
                        sb.AppendLine($"**{f.OperationName}** (`trace={f.TraceId}` `span={f.SpanId}`) carries {f.Links!.Count()} link(s):");
                        foreach (var link in f.Links!)
                            sb.AppendLine($"- `trace={link.Context.TraceId}` `span={link.Context.SpanId}`");
                        sb.AppendLine();
                    }
                }
            }

            // Only overwrite the committed sample file when explicitly asked to regenerate —
            // otherwise every test run would churn the files with fresh TraceIds and show up
            // as a noisy diff in git status. The scenario graph still runs and the tree still
            // builds every time; we just skip the disk write unless the author wants to
            // refresh the documentation artifacts.
            if (Environment.GetEnvironmentVariable("AKKA_STREAMS_RENDER_TRACE_SAMPLES") == "1")
            {
                var dir = Path.Combine(Path.GetDirectoryName(callerFilePath)!, "trace-samples");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, $"{scenario}.md");
                File.WriteAllText(path, sb.ToString());
                Output.WriteLine($"[rendered] {path}");
            }
            else
            {
                Output.WriteLine($"[rendered, not written — set AKKA_STREAMS_RENDER_TRACE_SAMPLES=1 to overwrite trace-samples/{scenario}.md]");
                Output.WriteLine(sb.ToString());
            }
        }

        private sealed class Node
        {
            public Activity Activity { get; set; }
            public List<Node> Children { get; } = new();
        }

        private static List<Node> BuildSpanTree(IReadOnlyList<Activity> spans)
        {
            var bySpanId = spans.ToDictionary(a => a.SpanId, a => new Node { Activity = a });
            var roots = new List<Node>();
            foreach (var node in bySpanId.Values)
            {
                var parentId = node.Activity.ParentSpanId;
                if (parentId != default && bySpanId.TryGetValue(parentId, out var parent))
                    parent.Children.Add(node);
                else
                    roots.Add(node);
            }
            return roots
                .OrderBy(n => n.Activity.StartTimeUtc)
                .ToList();
        }

        private static void RenderTree(List<Node> nodes, int depth, StringBuilder sb)
        {
            foreach (var node in nodes)
            {
                var indent = new string(' ', depth * 2);
                var a = node.Activity;
                var linkCount = a.Links?.Count() ?? 0;
                var linkSuffix = linkCount > 0 ? $"  [links: {linkCount}]" : "";
                sb.AppendLine($"{indent}├─ {a.OperationName}{linkSuffix}");
                sb.AppendLine($"{indent}│     trace={a.TraceId} span={a.SpanId}");
                RenderTree(node.Children.OrderBy(c => c.Activity.StartTimeUtc).ToList(), depth + 1, sb);
            }
        }
    }
}
