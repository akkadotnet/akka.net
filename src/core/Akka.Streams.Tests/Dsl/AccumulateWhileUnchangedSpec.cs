//-----------------------------------------------------------------------
// <copyright file="AccumulateWhileUnchangedSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Collections.Generic;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.Dsl
{
    public class AccumulateWhileUnchangedSpec : Akka.TestKit.Xunit2.TestKit
    {
        private struct Element
        {
            public int Id;
            public int Value;

            public Element(int id, int value)
            {
                Id = id;
                Value = value;
            }
        }

        private static class SampleElements
        {
            public static Element[] Ones => new[] { new Element(1, 1), new Element(2, 1), new Element(3, 1) };
            public static Element[] Twos => new[] { new Element(4, 2), new Element(5, 2) };
            public static Element[] Threes => new[] { new Element(6, 3) };

            public static IEnumerable<Element> All = Ones.Concat(Twos).Concat(Threes);
        }

        [Fact]
        public void AccumulateWhileUnchanged_should_emit_accumulated_elements_when_the_given_property_changes()
        {
            var sink = Source.From(SampleElements.All)
                .Via(new AccumulateWhileUnchanged<Element, int>(x => x.Value))
                .ToMaterialized(this.SinkProbe<IEnumerable<Element>>(), Keep.Right)
                .Run(Sys.Materializer());

            sink.Request(42);

            sink.ExpectNext().Should().Equal(SampleElements.Ones);
            sink.ExpectNext().Should().Equal(SampleElements.Twos);
            sink.ExpectNext().Should().Equal(SampleElements.Threes);
            sink.ExpectComplete();
        }

        [Fact]
        public void AccumulateWhileUnchanged_should_not_emit_any_value_for_an_empty_source()
        {
            var sink = Source.Empty<Element>()
                .Via(new AccumulateWhileUnchanged<Element, int>(x => x.Value))
                .RunWith(this.SinkProbe<IEnumerable<Element>>(), Sys.Materializer())
                .Request(42)
                .ExpectComplete();
        }

        [Fact]
        public void AccumulateWhileUnchanged_should_fail_on_upstream_failure()
        {
            var probe = this.SourceProbe<Element>()
                .Via(new AccumulateWhileUnchanged<Element, int>(x => x.Value))
                .ToMaterialized(this.SinkProbe<IEnumerable<Element>>(), Keep.Both)
                .Run(Sys.Materializer());

            var source = probe.Item1;
            var sink = probe.Item2;

            sink.Request(42);
            source.SendError(new Exception());
            sink.ExpectError();
        }
    }
}
