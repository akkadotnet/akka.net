//-----------------------------------------------------------------------
// <copyright file="ActorMaterializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
using FluentAssertions.Extensions;
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
        public async Task ActorMaterializer_should_properly_shut_down_actors_associated_with_it()
        {
            var m = Sys.Materializer();
            var f = Source.Maybe<int>().RunAggregate(0, (x, y) => x + y, m);

            m.Shutdown();

            Func<Task> task = () => f.ShouldCompleteWithin(3.Seconds());
            await task.Should().ThrowAsync<AbruptTerminationException>();
        }

        [Fact]
        public void ActorMaterializer_should_refuse_materialization_after_shutdown()
        {
            var m = Sys.Materializer();
            m.Shutdown();

            Action action = () => Source.From(Enumerable.Range(1, 5)).RunForeach(Console.Write, m);
            action.Should().Throw<IllegalStateException>();
        }

        [Fact]
        public async Task ActorMaterializer_should_shut_down_supervisor_actor_it_encapsulates()
        {
            var m = (ActorMaterializerImpl) Sys.Materializer();
            Source.From(Enumerable.Empty<object>()).To(Sink.Ignore<object>()).Run(m);

            m.Supervisor.Tell(StreamSupervisor.GetChildren.Instance);
            await ExpectMsgAsync<StreamSupervisor.Children>();
            m.Shutdown();

            m.Supervisor.Tell(StreamSupervisor.GetChildren.Instance);
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void ActorMaterializer_should_handle_properly_broken_Props()
        {
            var m = Sys.Materializer();
            Action action = () => Source.ActorPublisher<object>(Props.Create(typeof(TestActor), "wrong", "args")).RunWith(Sink.First<object>(), m);
            action.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void ActorMaterializer_should_report_correctly_if_it_has_been_shut_down_from_the_side()
        {
            var sys = ActorSystem.Create("test-system");
            var m = sys.Materializer();
            sys.Terminate().Wait();
            m.IsShutdown.Should().BeTrue();
        }

        [Fact]
        public async Task CanMaterializeStreamsUsingActorSystem()
        {
            Func<Task> task = () => Source.Single(1).RunForeach(_ => { }, Sys);
            await task.Should().NotThrowAsync();
        }
        
        [Fact]
        public void ShouldReturnSameMaterializerForActorSystem()
        {
            var mat1 = Sys.Materializer();
            var mat2 = Sys.Materializer();
            var mat3 = Sys.Materializer(namePrefix: "different");
            
            mat1.Should().Be(mat2);
            mat1.Should().NotBe(mat3);
        }
    }
}
