//-----------------------------------------------------------------------
// <copyright file="Utils.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Streams.Implementation;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using Akka.Util.Internal;
using FluentAssertions.Extensions;

namespace Akka.Streams.TestKit
{
    public static class Utils
    {
        public static Config UnboundedMailboxConfig { get; } =
            ConfigurationFactory.ParseString(@"akka.actor.default-mailbox.mailbox-type = ""Akka.Dispatch.UnboundedMailbox, Akka""");

        public static void AssertAllStagesStopped(
            this AkkaSpec spec,
            Action block,
            IMaterializer materializer,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
            => AssertAllStagesStoppedAsync(spec, () =>
                {
                    block();
                    return NotUsed.Instance;
                }, materializer, timeout, cancellationToken)
                .ConfigureAwait(false).GetAwaiter().GetResult();

        public static T AssertAllStagesStopped<T>(
            this AkkaSpec spec,
            Func<T> block,
            IMaterializer materializer,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
            => AssertAllStagesStoppedAsync(spec, async () => block(), materializer, timeout, cancellationToken)
                .ConfigureAwait(false).GetAwaiter().GetResult();

        public static async Task<T> AssertAllStagesStoppedAsync<T>(
            this AkkaSpec spec,
            Func<T> block,
            IMaterializer materializer,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
            => await AssertAllStagesStoppedAsync(spec, () => Task.FromResult(block()), materializer, timeout, cancellationToken)
                .ConfigureAwait(false);
        
        public static async Task<T> AssertAllStagesStoppedAsync<T>(
            this AkkaSpec spec,
            Func<Task<T>> block, 
            IMaterializer materializer,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            timeout ??= 20.Seconds();
            var result = await block().ShouldCompleteWithin(timeout.Value);
            if (!(materializer is ActorMaterializerImpl impl))
                return result;

            var probe = spec.CreateTestProbe(impl.System);
            probe.Send(impl.Supervisor, StreamSupervisor.StopChildren.Instance);
            await probe.ExpectMsgAsync<StreamSupervisor.StoppedChildren>(cancellationToken: cancellationToken);

            await probe.WithinAsync(TimeSpan.FromSeconds(5), async () =>
            {
                IImmutableSet<IActorRef> children = ImmutableHashSet<IActorRef>.Empty;
                try
                {
                    await probe.AwaitAssertAsync(async () =>
                    {
                        impl.Supervisor.Tell(StreamSupervisor.GetChildren.Instance, probe.Ref);
                        children = (await probe.ExpectMsgAsync<StreamSupervisor.Children>(cancellationToken: cancellationToken)).Refs;
                        if (children.Count != 0)
                        {
                            children.ForEach(c=>c.Tell(StreamSupervisor.PrintDebugDump.Instance));
                            await Task.Delay(100);
                            throw new Exception(
                                $"expected no StreamSupervisor children, but got {children.Aggregate("", (s, @ref) => s + @ref + ", ")}");
                        }
                    }, cancellationToken: cancellationToken);
                }
                catch 
                {
                    children.ForEach(c=>c.Tell(StreamSupervisor.PrintDebugDump.Instance));
                    await Task.Delay(100);
                    throw;
                }
            }, cancellationToken: cancellationToken);

            return result;
        }

        public static void AssertDispatcher(IActorRef @ref, string dispatcher)
        {
            if (!(@ref is ActorRefWithCell r))
                throw new Exception($"Unable to determine dispatcher of {@ref}");

            if (r.Underlying.Props.Dispatcher != dispatcher)
                throw new Exception($"Expected {@ref} to use dispatcher [{dispatcher}], yet used : [{r.Underlying.Props.Dispatcher}]");
        }

        [Obsolete("Use ShouldCompleteWithin instead")]
        public static T AwaitResult<T>(this Task<T> task, TimeSpan? timeout = null)
        {
            task.Wait(timeout??TimeSpan.FromSeconds(3)).ShouldBeTrue();
            return task.Result;
        }
    }
}
