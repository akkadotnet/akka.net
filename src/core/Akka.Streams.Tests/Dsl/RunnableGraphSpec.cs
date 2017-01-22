//-----------------------------------------------------------------------
// <copyright file="RunnableGraphSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
        public void A_RunnableGraph_must_suitably_override_attibute_handling_methods()
        {
            var r =
                RunnableGraph.FromGraph(Source.Empty<int>().To(Sink.Ignore<int>()))
                    .Async()
                    .AddAttributes(Attributes.None)
                    .Named("useless");

            r.Module.Attributes.GetFirstAttribute<Attributes.Name>().Value.Should().Be("useless");
            r.Module.Attributes.GetFirstAttribute<Attributes.AsyncBoundary>()
                .Should()
                .Be(Attributes.AsyncBoundary.Instance);
        }
    }
}
