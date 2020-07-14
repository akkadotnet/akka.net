// //-----------------------------------------------------------------------
// // <copyright file="BuilderForwardOptsConstraintsSpecs.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using Akka.Streams.Dsl;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class BuilderForwardOptsConstraintsSpecs : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public BuilderForwardOptsConstraintsSpecs(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }
        
        [Fact]
        public void Should_allow_convert_child_to_base_class()
        {
            GraphDsl.Create(builder =>
            {
                // This line should compile
                builder.From(Source.Single(new Child())).To(Sink.Last<Base>());

                return ClosedShape.Instance;
            });
        }

        public class Base { }
        public class Child : Base { }
    }
}