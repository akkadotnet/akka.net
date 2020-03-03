//-----------------------------------------------------------------------
// <copyright file="AttributesSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
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
                Source.Empty<NotUsed>()
                    .ToMaterialized(AttributesSink.Create().WithAttributes(Attributes.CreateName("new-name")),
                        Keep.Right);
            var task = runnable.Run(Materializer);

            task.AwaitResult().GetAttribute<Attributes.Name>().Value.Should().Contain("new-name");
        }

        [Fact]
        public void Attributes_must_keep_the_outermost_attribute_as_the_least_specific()
        {
            var task = Source.Empty<NotUsed>()
                .ToMaterialized(AttributesSink.Create(), Keep.Right)
                .WithAttributes(Attributes.CreateName("new-name"))
                .Run(Materializer);
            
            task.AwaitResult().GetAttribute<Attributes.Name>().Value.Should().Contain("attributesSink");
        }

        [Fact]
        public void Attributes_must_give_access_to_first_attribute()
            => Attributes.GetFirstAttribute<Attributes.Name>().Value.Should().Be("a");

        [Fact]
        public void Attributes_must_give_access_to_attribute_by_type()
            => Attributes.GetAttribute<Attributes.Name>().Value.Should().Be("b");

        private sealed class AttributesSink : SinkModule<NotUsed, Task<Attributes>>
        {
            public static Sink<NotUsed, Task<Attributes>> Create() =>
                    new Sink<NotUsed, Task<Attributes>>(new AttributesSink(
                        Attributes.CreateName("attributesSink"),
                        new SinkShape<NotUsed>(new Inlet<NotUsed>("attributesSink"))));

            private AttributesSink(Attributes attributes, SinkShape<NotUsed> shape) : base(shape)
            {
                Attributes = attributes;
            }

            public override Attributes Attributes { get; }

            public override IModule WithAttributes(Attributes attributes)
                => new AttributesSink(attributes, AmendShape(attributes));

            protected override SinkModule<NotUsed, Task<Attributes>> NewInstance(SinkShape<NotUsed> shape)
                => new AttributesSink(Attributes, shape);

            public override object Create(MaterializationContext context, out Task<Attributes> materializer)
            {
                materializer = Task.FromResult(context.EffectiveAttributes);
                return new SinkholeSubscriber<NotUsed>(new TaskCompletionSource<NotUsed>());
            }
        }
    }
}
