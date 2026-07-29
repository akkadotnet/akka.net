// -----------------------------------------------------------------------
//  <copyright file="PromiseActorRefSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Actor;

public class PromiseActorRefSpec : AkkaSpec
{
    public PromiseActorRefSpec(ITestOutputHelper output) : base(output)
    {
    }

    /// <summary>
    /// Regression test for the race between <see cref="PromiseActorRef.Stop"/> and any read of
    /// <see cref="PromiseActorRef.Path"/>.
    ///
    /// <c>GetPath</c> used to pattern-match on its <c>State</c> field and then re-read that field
    /// to produce the return value (<c>return State as ActorPath;</c>). A concurrent <c>Stop()</c>
    /// flipping the state from <c>ActorPath</c> to <c>StoppedWithPath</c> in between those two
    /// reads made the cast fail and <c>Path</c> return <c>null</c>, which then blew up inside
    /// <c>ActorRefBase.GetHashCode()</c> - the exact frame that threw in CI, reached via
    /// <c>HashSet&lt;IActorRef&gt;.Remove</c> in <c>FullActorState.RemoveWatchedBy</c>.
    ///
    /// Each round pins one <see cref="PromiseActorRef"/> between two threads: the reader spins on
    /// <c>Path</c>/<c>GetHashCode</c> and the stopper waits until the reader is demonstrably inside
    /// that spin before flipping the state, so the flip always lands while a read is in flight.
    /// </summary>
    [Fact(DisplayName =
        "PromiseActorRef.Path should never be null when a Path read races with a concurrent Stop")]
    public async Task Should_never_return_null_Path_when_Path_read_races_with_Stop()
    {
        const int rounds = 2_000;
        const int trailingReads = 32;
        const int maxSpinsPerRound = 100_000; // safety valve, never expected to be hit

        var provider = ((ExtendedActorSystem)Sys).Provider;
        var barrier = new Barrier(2);

        PromiseActorRef? current = null;
        var readerSpinning = 0;
        var stopped = 0;
        var nullPaths = 0;
        var failures = 0;
        Exception? firstFailure = null;

        void RecordFailure(Exception ex)
        {
            Interlocked.Increment(ref failures);
            Interlocked.CompareExchange(ref firstFailure, ex, null);
        }

        // reads Path (GetHashCode dereferences it twice) while the other thread stops the ref
        var reader = Task.Factory.StartNew(() =>
        {
            var checksum = 0;
            for (var round = 0; round < rounds; round++)
            {
                barrier.SignalAndWait();
                var promiseRef = Volatile.Read(ref current)!;
                Volatile.Write(ref readerSpinning, 1);

                var remaining = trailingReads;
                for (var spin = 0; spin < maxSpinsPerRound && remaining > 0; spin++)
                {
                    // keep reading for a few iterations past the Stop() so the flip is always
                    // bracketed by reads on this thread
                    if (Volatile.Read(ref stopped) == 1)
                        remaining--;

                    try
                    {
                        checksum ^= promiseRef.GetHashCode();
                        if (promiseRef.Path is null)
                            Interlocked.Increment(ref nullPaths);
                    }
                    catch (Exception ex)
                    {
                        RecordFailure(ex);
                    }
                }

                barrier.SignalAndWait();
            }

            return checksum;
        }, TaskCreationOptions.LongRunning);

        var stopper = Task.Factory.StartNew(() =>
        {
            for (var round = 0; round < rounds; round++)
            {
                var promiseRef = PromiseActorRef.Apply(provider, nameof(PromiseActorRefSpec));

                // force the temp path to be created and registered, so State == ActorPath
                _ = promiseRef.Path;
                Volatile.Write(ref current, promiseRef);
                Volatile.Write(ref readerSpinning, 0);
                Volatile.Write(ref stopped, 0);

                barrier.SignalAndWait();

                // don't flip the state until the reader is actually inside its read loop
                var spinner = new SpinWait();
                while (Volatile.Read(ref readerSpinning) == 0)
                    spinner.SpinOnce();

                promiseRef.Stop(); // State: ActorPath -> StoppedWithPath
                Volatile.Write(ref stopped, 1);

                barrier.SignalAndWait();
            }
        }, TaskCreationOptions.LongRunning);

        await Task.WhenAll(reader, stopper);

        var checksum = await reader;
        Output?.WriteLine("Completed {0} rounds, checksum: {1}, null paths: {2}, exceptions: {3}{4}",
            rounds, checksum, Volatile.Read(ref nullPaths), Volatile.Read(ref failures),
            Volatile.Read(ref firstFailure) is { } failure ? Environment.NewLine + failure : string.Empty);

        Volatile.Read(ref nullPaths).Should()
            .Be(0, "PromiseActorRef.Path must never observe a torn read of its own state");
        Volatile.Read(ref failures).Should()
            .Be(0, "reading Path while a PromiseActorRef stops must not throw, but got: {0}",
                Volatile.Read(ref firstFailure));
    }
}
