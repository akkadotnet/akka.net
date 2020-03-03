//-----------------------------------------------------------------------
// <copyright file="ActorMaterializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests
{
    public class ActorMaterializerSpec : AkkaSpec
    {
        public ActorMaterializerSpec(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void ActorMaterializer_should_report_shutdown_status_properly()
        {
            var m = Sys.Materializer();

            m.IsShutdown.Should().BeFalse();
            m.Shutdown();
            m.IsShutdown.Should().BeTrue();
        }

        [Fact]
        public void ActorMaterializer_should_properly_shut_down_actors_associated_with_it()
        {
            var m = Sys.Materializer();
            var f = Source.Maybe<int>().RunAggregate(0, (x, y) => x + y, m);

            m.Shutdown();

            Action action = () => f.Wait(TimeSpan.FromSeconds(3));
            action.ShouldThrow<AbruptTerminationException>();
        }

        [Fact]
        public void ActorMaterializer_should_refuse_materialization_after_shutdown()
        {
            var m = Sys.Materializer();
            m.Shutdown();

            Action action = () => Source.From(Enumerable.Range(1, 5)).RunForeach(Console.Write, m);
            action.ShouldThrow<IllegalStateException>();
        }

        [Fact]
        public void ActorMaterializer_should_shut_down_supervisor_actor_it_encapsulates()
        {
            var m = Sys.Materializer() as ActorMaterializerImpl;
            Source.From(Enumerable.Empty<object>()).To(Sink.Ignore<object>()).Run(m);

            m.Supervisor.Tell(StreamSupervisor.GetChildren.Instance);
            ExpectMsg<StreamSupervisor.Children>();
            m.Shutdown();

            m.Supervisor.Tell(StreamSupervisor.GetChildren.Instance);
            ExpectNoMsg(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void ActorMaterializer_should_handle_properly_broken_Props()
        {
            var m = Sys.Materializer();
            Action action = () => Source.ActorPublisher<object>(Props.Create(typeof(TestActor), "wrong", "args")).RunWith(Sink.First<object>(), m);
            action.ShouldThrow<ArgumentException>();
        }

        [Fact]
        public void ActorMaterializer_should_report_correctly_if_it_has_been_shut_down_from_the_side()
        {
            var sys = ActorSystem.Create("test-system");
            var m = sys.Materializer();
            sys.Terminate().Wait();
            m.IsShutdown.Should().BeTrue();
        }
    }
}
