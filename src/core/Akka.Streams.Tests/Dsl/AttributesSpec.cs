//-----------------------------------------------------------------------
// <copyright file="AttributesSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Reactive.Streams;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.TestKit.Tests;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.Dsl
{
    public class AttributesSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public AttributesSpec()
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        private static Attributes Attributes => 
            Attributes.CreateName("a").And(Attributes.CreateName("b")).And(Attributes.CreateInputBuffer(1, 2));

        [Fact]
        public void Attributes_must_be_overridable_on_a_module_basis()
        {
            var runnable =
                Source.Empty<Unit>()
                    .ToMaterialized(AttributesSink.Create().WithAttributes(Attributes.CreateName("new-name")),
                        Keep.Right);
            var task = runnable.Run(Materializer);
            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            task.Result.GetAttribute<Attributes.Name>().Value.Should().Contain("new-name");
        }

        [Fact]
        public void Attributes_must_keep_the_outermost_attribute_as_the_least_specific()
        {
            var runnable =
                RunnableGraph.FromGraph(
                    Source.Empty<Unit>()
                        .ToMaterialized(AttributesSink.Create(), Keep.Right)
                        .WithAttributes(Attributes.CreateName("new-name")));
            var task = runnable.Run(Materializer);
            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            task.Result.GetAttribute<Attributes.Name>().Value.Should().Contain("attributesSink");
        }

        [Fact]
        public void Attributes_must_give_access_to_first_attribute()
            => Attributes.GetFirstAttribute<Attributes.Name>().Value.Should().Be("a");

        [Fact]
        public void Attributes_must_give_access_to_attribute_by_type()
            => Attributes.GetAttribute<Attributes.Name>().Value.Should().Be("b");

        private sealed class AttributesSink : SinkModule<Unit, Task<Attributes>>
        {
            public static Sink<Unit, Task<Attributes>> Create() =>
                    new Sink<Unit, Task<Attributes>>(new AttributesSink(
                        Attributes.CreateName("attributesSink"),
                        new SinkShape<Unit>(new Inlet<Unit>("attributesSink"))));

            private AttributesSink(Attributes attributes, SinkShape<Unit> shape) : base(shape)
            {
                Attributes = attributes;
            }

            public override Attributes Attributes { get; }

            public override IModule WithAttributes(Attributes attributes)
                => new AttributesSink(attributes, AmendShape(attributes));

            protected override SinkModule<Unit, Task<Attributes>> NewInstance(SinkShape<Unit> shape)
                => new AttributesSink(Attributes, shape);

            public override ISubscriber<Unit> Create(MaterializationContext context, out Task<Attributes> materializer)
            {
                materializer = Task.FromResult(context.EffectiveAttributes);
                return new SinkholeSubscriber<Unit>(new TaskCompletionSource<Unit>());
            }
        }
    }
}
