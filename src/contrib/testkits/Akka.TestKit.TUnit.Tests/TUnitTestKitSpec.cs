//-----------------------------------------------------------------------
// <copyright file="TUnitTestKitSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit.TestActors;
using TUnit.Assertions;
using TUnit.Assertions.Exceptions;
using TUnit.Core;

namespace Akka.TestKit.TUnit.Tests;

public sealed class TUnitTestKitSpec : Akka.TestKit.TUnit.TestKit
{
    private bool HasTestOutput => Output is not null;

    [Test]
    public async Task Should_exchange_actor_messages_with_async_testkit_apis()
    {
        var actor = Sys.ActorOf(SimpleEchoActor.Props());
        await Task.Yield();

        actor.Tell("hello");

        await ExpectMsgAsync("hello", TimeSpan.FromSeconds(3));
        await Assert.That(HasTestOutput).IsTrue();
    }

    [Test]
    public async Task Should_report_testkit_failures_as_tunit_assertion_exceptions()
    {
        var exception = await Assert.ThrowsAsync<AssertionException>(
            () => Assertions.AssertThrowsAsync<InvalidOperationException>(() => Task.CompletedTask));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains(nameof(InvalidOperationException));
    }

    [Test]
    public async Task Should_capture_actor_logs_in_tunit_output()
    {
        var marker = $"tunit-log-{Guid.NewGuid():N}";

        Sys.Log.Info(marker);

        await AwaitAssertAsync(
            () => Assertions.AssertTrue(TestContext.Current!.Output.GetStandardOutput().Contains(marker)),
            TimeSpan.FromSeconds(3),
            TimeSpan.FromMilliseconds(50));
    }

    [Test]
    public async Task Should_accept_tunit_test_cancellation_token()
        => await ExpectNoMsgAsync(
            TimeSpan.FromMilliseconds(20),
            TestContext.Current!.Execution.CancellationToken);
}
