using System.Collections.Immutable;
using System.Linq;
using System.Reactive.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.Implementation
{
    public class StreamLayoutSpec : Akka.TestKit.Xunit2.TestKit
    {
        private class TestAtomicModule : Module
        {
            public TestAtomicModule(int inportCount, int outportCount)
            {
                var inports = Enumerable.Range(0, inportCount).Select(i => new Inlet<object>(".in" + i)).ToImmutableArray<Inlet>();
                var outports = Enumerable.Range(0, outportCount).Select(i => new Outlet<object>(".out" + i)).ToImmutableArray<Outlet>();

                Shape = new AmorphousShape(inports, outports);
            }

            public override Shape Shape { get; }
            public override IModule ReplaceShape(Shape shape)
            {
                throw new System.NotImplementedException();
            }

            public override ImmutableArray<IModule> SubModules => ImmutableArray<IModule>.Empty;

            public override IModule CarbonCopy()
            {
                throw new System.NotImplementedException();
            }

            public override Attributes Attributes => Attributes.None;
            public override IModule WithAttributes(Attributes attributes)
            {
                return this;
            }
        }

        private static TestAtomicModule TestStage() => new TestAtomicModule(1, 1);
        private static TestAtomicModule TestSource() => new TestAtomicModule(0, 1);
        private static TestAtomicModule TestSink() => new TestAtomicModule(1, 0);

        [Fact]
        public void StreamLayout_should_be_able_to_model_simple_linear_stages()
        {
            var stage1 = TestStage();

            stage1.InPorts.Count.Should().Be(1);
            stage1.OutPorts.Count.Should().Be(1);
            stage1.IsRunnable.Should().Be(false);
            stage1.IsFlow.Should().Be(true);
            stage1.IsSink.Should().Be(false);
            stage1.IsSource.Should().Be(false);

            var stage2 = TestStage();
            var flow12 = stage1.Compose<object, object, Unit>(stage2, Keep.None).Wire(stage1.OutPorts.First(), stage2.InPorts.First());

            flow12.InPorts.Should().BeEquivalentTo(stage1.InPorts);
            flow12.OutPorts.Should().BeEquivalentTo(stage2.OutPorts);
            flow12.IsRunnable.Should().Be(false);
            flow12.IsFlow.Should().Be(true);
            flow12.IsSink.Should().Be(false);
            flow12.IsSource.Should().Be(false);

            var source0 = TestSource();

            source0.InPorts.Count.Should().Be(0);
            source0.OutPorts.Count.Should().Be(1);
            source0.IsRunnable.Should().Be(false);
            source0.IsFlow.Should().Be(false);
            source0.IsSink.Should().Be(false);
            source0.IsSource.Should().Be(true);

            var sink3 = TestSink();

            sink3.InPorts.Count.Should().Be(1);
            sink3.OutPorts.Count.Should().Be(0);
            sink3.IsRunnable.Should().Be(false);
            sink3.IsFlow.Should().Be(false);
            sink3.IsSink.Should().Be(true);
            sink3.IsSource.Should().Be(false);

            var source012 = source0.Compose<object, object, Unit>(flow12, Keep.None).Wire(source0.OutPorts.First(), flow12.InPorts.First());

            source012.InPorts.Count.Should().Be(0);
            source012.OutPorts.Should().BeEquivalentTo(flow12.OutPorts);
            source012.IsRunnable.Should().Be(false);
            source012.IsFlow.Should().Be(false);
            source012.IsSink.Should().Be(false);
            source012.IsSource.Should().Be(true);

            var sink123 = flow12.Compose<object, object, Unit>(sink3, Keep.None).Wire(flow12.OutPorts.First(), sink3.InPorts.First());

            source012.InPorts.Should().BeEquivalentTo(flow12.InPorts);
            source012.OutPorts.Count.Should().Be(0);
            source012.IsRunnable.Should().Be(false);
            source012.IsFlow.Should().Be(false);
            source012.IsSink.Should().Be(true);
            source012.IsSource.Should().Be(false);

            var runnable0123a = source0.Compose<object, object, Unit>(sink123, Keep.None).Wire(source0.OutPorts.First(), sink123.InPorts.First());
            var runnable0123b = source012.Compose<object, object, Unit>(sink3, Keep.None).Wire(source012.OutPorts.First(), sink3.InPorts.First());
            var runnable0123c = source0
                .Compose<object, object, Unit>(flow12, Keep.None).Wire(source0.OutPorts.First(), flow12.InPorts.First())
                .Compose<object, object, Unit>(sink3, Keep.None).Wire(flow12.OutPorts.First(), sink3.InPorts.First());

            runnable0123a.InPorts.Count.Should().Be(0);
            runnable0123a.OutPorts.Count.Should().Be(0);
            runnable0123a.IsRunnable.Should().Be(true);
            runnable0123a.IsFlow.Should().Be(false);
            runnable0123a.IsSink.Should().Be(false);
            runnable0123a.IsSource.Should().Be(false);
        }
    }
}