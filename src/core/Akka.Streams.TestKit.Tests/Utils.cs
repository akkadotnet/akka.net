//-----------------------------------------------------------------------
// <copyright file="Utils.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Streams.Implementation;
using Akka.TestKit;
using Akka.Util.Internal;

namespace Akka.Streams.TestKit.Tests
{
    public static class Utils
    {
        public static Config UnboundedMailboxConfig { get; } =
            ConfigurationFactory.ParseString(@"akka.actor.default-mailbox.mailbox-type = ""Akka.Dispatch.UnboundedMailbox, Akka""");

        public static void AssertAllStagesStopped(this AkkaSpec spec, Action block, IMaterializer materializer)
        {
            AssertAllStagesStopped(spec, () =>
            {
                block();
                return NotUsed.Instance;
            }, materializer);
        }

        public static T AssertAllStagesStopped<T>(this AkkaSpec spec, Func<T> block, IMaterializer materializer)
        {
            if (!(materializer is ActorMaterializerImpl impl))
                return block();

            var probe = spec.CreateTestProbe(impl.System);
            probe.Send(impl.Supervisor, StreamSupervisor.StopChildren.Instance);
            probe.ExpectMsg<StreamSupervisor.StoppedChildren>();
            var result = block();

            probe.Within(TimeSpan.FromSeconds(5), () =>
            {
                IImmutableSet<IActorRef> children = ImmutableHashSet<IActorRef>.Empty;
                try
                {
                    probe.AwaitAssert(() =>
                    {
                        impl.Supervisor.Tell(StreamSupervisor.GetChildren.Instance, probe.Ref);
                        children = probe.ExpectMsg<StreamSupervisor.Children>().Refs;
                        if (children.Count != 0)
                            throw new Exception($"expected no StreamSupervisor children, but got {children.Aggregate("", (s, @ref) => s + @ref + ", ")}");
                    });
                }
                catch 
                {
                    children.ForEach(c=>c.Tell(StreamSupervisor.PrintDebugDump.Instance));
                    throw;
                }
            });

            return result;
        }

        public static void AssertDispatcher(IActorRef @ref, string dispatcher)
        {
            var r = @ref as ActorRefWithCell;

            if (r == null)
                throw new Exception($"Unable to determine dispatcher of {@ref}");

            if (r.Underlying.Props.Dispatcher != dispatcher)
                throw new Exception($"Expected {@ref} to use dispatcher [{dispatcher}], yet used : [{r.Underlying.Props.Dispatcher}]");
        }

        public static T AwaitResult<T>(this Task<T> task, TimeSpan? timeout = null)
        {
            task.Wait(timeout??TimeSpan.FromSeconds(3)).ShouldBeTrue();
            return task.Result;
        }
    }
}
