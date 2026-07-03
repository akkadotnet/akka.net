//-----------------------------------------------------------------------
// <copyright file="ArterySubstrateFixture.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;

namespace Akka.Benchmarks.Remoting.Artery
{
    /// <summary>
    /// Shared constants and helpers for the Task 0 transport-substrate validation benchmarks
    /// (<c>openspec/changes/artery-tcp-remoting/tasks.md</c>, gate G0). All three configs push
    /// the same corpus through the same decode/deserialize/dispatch work — only the plumbing
    /// (mailboxes vs interpreter islands vs lanes) differs.
    /// </summary>
    public static class ArterySubstrateFixture
    {
        /// <summary>Messages per corpus pass; must divide evenly by <see cref="RecipientCount"/>.</summary>
        public const int CorpusMessageCount = 102_400;

        /// <summary>Corpus passes per benchmark invocation.</summary>
        public const int Repeat = 10;

        /// <summary>Total messages per benchmark invocation (= OperationsPerInvoke).</summary>
        public const int TotalMessages = CorpusMessageCount * Repeat;

        /// <summary>Distinct recipient actors; recipients are round-robined across the corpus.</summary>
        public const int RecipientCount = 64;

        public const int ExpectedPerRecipient = TotalMessages / RecipientCount;

        /// <summary>Small payload matching the RemotePingPong-class messages behind the 680K baseline.</summary>
        public const int PayloadSize = 32;

        /// <summary>
        /// Simulated TCP read size. Deliberately NOT a multiple of the 64-byte frame size so
        /// frames span chunk boundaries — exercising the framing stage's reassembly path and
        /// the codec's multi-segment header fallback, exactly as unaligned socket reads do.
        /// </summary>
        public const int ChunkSize = 60_000;

        public const int MaxFrameLength = 512;

        /// <summary>Bound for the chunk-level ingress channel (chunks in flight, not messages).</summary>
        public const int ChunkChannelCapacity = 256;

        /// <summary>Bound for the per-message ingress channel/queue (task 0.4).</summary>
        public const int MessageChannelCapacity = 1024;

        public const string SystemConfig = "akka.log-dead-letters = off";

        private static int _spawnCounter;

        public static ArteryFrameCorpus CreateCorpus() =>
            new(CorpusMessageCount, RecipientCount, PayloadSize, ChunkSize);

        /// <summary>
        /// Spawns the counting recipient actors for one benchmark iteration. Each recipient
        /// knows its exact expected message count, so <paramref name="allDone"/> completing
        /// is also the correctness check: a single lost or misrouted message hangs the run.
        /// </summary>
        public static IActorRef[] SpawnRecipients(ActorSystem system, out Task allDone)
        {
            var iteration = Interlocked.Increment(ref _spawnCounter);
            var recipients = new IActorRef[RecipientCount];
            var tasks = new Task[RecipientCount];
            for (var i = 0; i < RecipientCount; i++)
            {
                var tcs = new TaskCompletionSource<Done>(TaskCreationOptions.RunContinuationsAsynchronously);
                tasks[i] = tcs.Task;
                recipients[i] = system.ActorOf(
                    Props.Create(() => new RecipientActor(ExpectedPerRecipient, tcs)),
                    $"recipient-{iteration}-{i}");
            }

            allDone = Task.WhenAll(tasks);
            return recipients;
        }

        public static void StopRecipients(ActorSystem system, IActorRef[]? recipients)
        {
            if (recipients is null) return;
            foreach (var recipient in recipients)
                system.Stop(recipient);
        }
    }

    /// <summary>
    /// Terminal "dispatch" actor standing in for the recipient of a remote message — the
    /// per-message mailbox hop that production Artery also pays at
    /// <c>messageDispatcherSink → MessageDispatcher.dispatch</c>. Counts envelopes and
    /// completes its task at exactly the expected count.
    /// </summary>
    public sealed class RecipientActor : UntypedActor
    {
        private readonly int _expected;
        private readonly TaskCompletionSource<Done> _done;
        private int _count;

        public RecipientActor(int expected, TaskCompletionSource<Done> done)
        {
            _expected = expected;
            _done = done;
        }

        protected override void OnReceive(object message)
        {
            if (message is InboundFrame && ++_count == _expected)
                _done.TrySetResult(Done.Instance);
        }
    }

    /// <summary>
    /// BenchmarkDotNet configuration for the substrate benchmarks: macro-style monitored
    /// iterations (one 1M-message invocation per iteration), ServerGC, MemoryDiagnoser for
    /// allocations/msg, and the Req/sec column for direct comparison against the 680K
    /// msgs/sec DotNetty baseline.
    /// </summary>
    public class ArterySubstrateConfig : ManualConfig
    {
        public ArterySubstrateConfig()
        {
            AddDiagnoser(MemoryDiagnoser.Default);
            AddExporter(MarkdownExporter.GitHub);
            AddLogger(ConsoleLogger.Default);
            AddColumn(new RequestsPerSecondColumn());
            AddJob(Job.Default
                .WithGcMode(new GcMode { Server = true, Concurrent = true })
                .WithWarmupCount(2)
                .WithIterationCount(10)
                .RunOncePerIteration()
                .WithStrategy(RunStrategy.Monitoring));
        }
    }
}
