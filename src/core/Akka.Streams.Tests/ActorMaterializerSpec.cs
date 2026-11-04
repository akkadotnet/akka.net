//-----------------------------------------------------------------------
// <copyright file="ActorMaterializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;

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

            Func<Task> task = () => f.WaitAsync(3.Seconds());
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

        [Fact(DisplayName = "Repeated ActorSystem.Materializer() calls should share one materializer and one StreamSupervisor")]
        public void Should_share_one_materializer_and_supervisor_When_Materializer_called_repeatedly_on_ActorSystem()
        {
            var first = (ActorMaterializerImpl)Sys.Materializer();
            var supervisorsBefore = StreamSupervisorCount(Sys);

            for (var i = 0; i < 100; i++)
            {
                var m = (ActorMaterializerImpl)Sys.Materializer();
                m.Should().BeSameAs(first);
                m.Supervisor.Should().Be(first.Supervisor);
            }

            StreamSupervisorCount(Sys).Should().Be(supervisorsBefore);
        }

        [Fact(DisplayName = "ActorSystem.Materializer() with custom settings should create a distinct materializer")]
        public void Should_create_distinct_materializer_When_Materializer_called_with_custom_settings()
        {
            var shared = (ActorMaterializerImpl)Sys.Materializer();
            var supervisorsBefore = StreamSupervisorCount(Sys);

            var custom = (ActorMaterializerImpl)Sys.Materializer(ActorMaterializerSettings.Create(Sys));
            try
            {
                custom.Should().NotBeSameAs(shared);
                custom.Supervisor.Should().NotBe(shared.Supervisor);
                StreamSupervisorCount(Sys).Should().Be(supervisorsBefore + 1);
            }
            finally
            {
                custom.Shutdown();
            }
        }

        [Fact(DisplayName = "Disposing the shared ActorSystem materializer should not shut it down")]
        public async Task Should_not_shut_down_shared_materializer_When_it_is_disposed()
        {
            using (var m = Sys.Materializer())
            {
                (await Source.Single(1).RunWith(Sink.First<int>(), m)).Should().Be(1);
            }

            // a name-prefixed view shares the shared materializer's supervisor
            Sys.Materializer(namePrefix: "prefixed").Dispose();
            ActorMaterializer.Create(Sys).Dispose();

            var shared = Sys.Materializer();
            shared.IsShutdown.Should().BeFalse();
            (await Source.Single(2).RunWith(Sink.First<int>(), shared)).Should().Be(2);
            (await Source.Single(3).RunWith(Sink.First<int>(), Sys)).Should().Be(3);
        }

        [Fact(DisplayName = "Disposing the shared ActorSystem materializer should log a debug message only once")]
        public async Task Should_log_debug_once_When_shared_materializer_is_disposed_repeatedly()
        {
            // AkkaSpec runs at WARNING, so use a dedicated system with DEBUG enabled to observe the log.
            var system = ActorSystem.Create("shared-materializer-dispose-log",
                ConfigurationFactory.ParseString("akka.loglevel = DEBUG").WithFallback(Sys.Settings.Config));
            try
            {
                await CreateEventFilter(system)
                    .Debug(start: "Ignored Dispose() on the ActorSystem's shared default materializer")
                    .ExpectOneAsync(() =>
                    {
                        system.Materializer().Dispose();
                        system.Materializer().Dispose();
                        system.Materializer(namePrefix: "prefixed").Dispose();
                        return Task.CompletedTask;
                    });
            }
            finally
            {
                await system.Terminate();
            }
        }

        [Fact(DisplayName = "Disposing a materializer created with custom settings should shut it down")]
        public void Should_shut_down_dedicated_materializer_When_it_is_disposed()
        {
            var custom = Sys.Materializer(ActorMaterializerSettings.Create(Sys));
            custom.Dispose();

            custom.IsShutdown.Should().BeTrue();
            Sys.Materializer().IsShutdown.Should().BeFalse();
        }

        [Fact(DisplayName = "The shared ActorSystem materializer should shut down when the ActorSystem terminates")]
        public async Task Should_shut_down_shared_materializer_When_ActorSystem_terminates()
        {
            var sys = ActorSystem.Create("shared-materializer-termination");
            var m = (ActorMaterializerImpl)sys.Materializer();
            m.IsShutdown.Should().BeFalse();

            await ShutdownAsync(sys, verifySystemShutdown: true);

            // nothing called Shutdown(), so IsShutdown can only flip via the StreamSupervisor's PostStop
            m.IsShutdown.Should().BeTrue();
        }

        private static int StreamSupervisorCount(ActorSystem system)
        {
            var guardian = (ActorRefWithCell)((ExtendedActorSystem)system).Guardian;
            return guardian.Children.Count(c => c.Path.Name.StartsWith("StreamSupervisor-"));
        }
    }
}
