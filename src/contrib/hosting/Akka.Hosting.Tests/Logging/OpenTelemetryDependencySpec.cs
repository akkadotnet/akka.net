// -----------------------------------------------------------------------
//  <copyright file="OpenTelemetryDependencySpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using Xunit;

namespace Akka.Hosting.Tests.Logging
{
    public sealed class OpenTelemetryDependencySpec
    {
        [Theory(DisplayName = "Should_EnforceBaggageHeaderLimit_When_UsingTheHostingDependencyGraph")]
        [InlineData(8190, true)]
        [InlineData(8192, false)]
        public void Should_EnforceBaggageHeaderLimit_When_UsingTheHostingDependencyGraph(int valueLength, bool shouldInject)
        {
            // GHSA-g94r-2vxg-569j: before API 1.15.3, a single entry bypassed the 8192-character limit.
            var value = new string('x', valueLength);
            var context = new PropagationContext(default, Baggage.Create(new Dictionary<string, string> { ["k"] = value }));
            var headers = new Dictionary<string, string>();

            new BaggagePropagator().Inject(context, headers, static (carrier, name, header) => carrier[name] = header);

            if (shouldInject)
            {
                headers.Should().HaveCount(1);
                headers.Should().Contain("baggage", "k=" + value);
            }
            else
            {
                headers.Should().BeEmpty();
            }
        }
    }
}
