//-----------------------------------------------------------------------
// <copyright file="RunnableGraphSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Streams.Dsl;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class RunnableGraphSpec : AkkaSpec
    {
        public RunnableGraphSpec(ITestOutputHelper helper) : base(helper)
        {

        }

        [Fact]
        public void A_RunnableGraph_must_suitably_override_attribute_handling_methods()
        {
            var r =
                RunnableGraph.FromGraph(Source.Empty<int>().To(Sink.Ignore<int>()))
                    .Async()
                    .AddAttributes(Attributes.None)
                    .Named("useless");

#pragma warning disable CS0618 // Type or member is obsolete
            r.Module.Attributes.GetFirstAttribute<Attributes.Name>().Value.Should().Be("useless");
#pragma warning restore CS0618 // Type or member is obsolete
#pragma warning disable CS0618 // Type or member is obsolete
            r.Module.Attributes.GetFirstAttribute<Attributes.AsyncBoundary>()
                .Should()
                .Be(Attributes.AsyncBoundary.Instance);
#pragma warning restore CS0618 // Type or member is obsolete
        }
    }
}
