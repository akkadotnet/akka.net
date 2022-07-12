// -----------------------------------------------------------------------
//  <copyright file="EventFilterSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.TestKit.Extensions;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;
using static FluentAssertions.FluentActions;

namespace Akka.TestKit.Tests.TestEventListenerTests
{
    public class EventFilterSpec: TestKit.Xunit2.TestKit
    {
        // A very short EventFilter leeway value
        public EventFilterSpec(ITestOutputHelper helper): base("akka.test.filter-leeway = 50ms", helper)
        {}
        
        [Fact(DisplayName = "ExpectOneAsync should respect WithinAsync block Remaining value and overrides the default filter-leeway settings")]
        public async Task ExpectOneRemainingTest()
        {
            await Awaiting(async () =>
            {
                await WithinAsync(3.Seconds(), async () =>
                {
                    var task = EventFilter.Info(start: "test message")
                        .ExpectOneAsync(() => Task.CompletedTask);

                    // This should NOT throw, the 100ms EventFilter leeway SHOULD be replaced with WithinAsync Remaining
                    await Task.Delay(200.Milliseconds());
                    Sys.Log.Info("test message");
                    await task.ShouldCompleteWithin(3.Seconds());
                });
            }).Should().NotThrowAsync();
        }
        
        [Fact(DisplayName = "ExpectAsync should respect WithinAsync block Remaining value and overrides the default filter-leeway settings")]
        public async Task ExpectRemainingTest()
        {
            await Awaiting(async () =>
            {
                await WithinAsync(3.Seconds(), async () =>
                {
                    var task = EventFilter.Info(start: "test message")
                        .ExpectAsync(2, () => Task.CompletedTask);

                    // This should NOT throw, the 100ms EventFilter leeway SHOULD be replaced with WithinAsync Remaining
                    await Task.Delay(200.Milliseconds());
                    Sys.Log.Info("test message");
                    Sys.Log.Info("test message");
                    await task.ShouldCompleteWithin(3.Seconds());
                });
            }).Should().NotThrowAsync();
        }
        
    }
}