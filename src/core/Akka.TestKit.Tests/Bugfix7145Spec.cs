//-----------------------------------------------------------------------
// <copyright file="Bugfix7145Spec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Actor;
using FluentAssertions;
using Xunit;

namespace Akka.TestKit.Tests;

public class Bugfix7145Spec : AkkaSpec
{
    // generate a test actor that will receive a message inside a ReceiveAsync - it should then await briefly on a Task.Delay inside that ReceiveAsync and then send two different messages back to the Sender
    private class BuggyActor : ReceiveActor
    {
        public BuggyActor()
        {
            ReceiveAsync<string>(async s =>
            {
                await Task.Delay(100);
                Sender.Tell(s + "1");
                Sender.Tell(s + "2");
            });
        }
    }
    
    [Fact]
    public async Task Should_not_deadlock_when_using_ReceiveAsync()
    {
        var actor = Sys.ActorOf(Props.Create(() => new BuggyActor()));
        var probe = CreateTestProbe();
        actor.Tell("hello", probe);
        var response1 = await probe.ExpectMsgAsync<string>();
        var response2 = await probe.ExpectMsgAsync<string>();
        response1.Should().Be("hello1");
        response2.Should().Be("hello2");
    }
    
    // generate a test case where we set ConfigureAwait(false) on the ExpectMsgAsync calls inside the test method
    [Fact]
    public async Task Should_not_deadlock_when_using_ReceiveAsync_with_ConfigureAwait_false()
    {
        var actor = Sys.ActorOf(Props.Create(() => new BuggyActor()));
        var probe = CreateTestProbe();
        actor.Tell("hello", probe);
        var response1 = await probe.ExpectMsgAsync<string>().ConfigureAwait(false);
        var response2 = await probe.ExpectMsgAsync<string>().ConfigureAwait(false);
        response1.Should().Be("hello1");
        response2.Should().Be("hello2");
    }
}
