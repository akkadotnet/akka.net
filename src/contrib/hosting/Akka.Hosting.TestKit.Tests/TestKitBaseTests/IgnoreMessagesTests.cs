//-----------------------------------------------------------------------
// <copyright file="IgnoreMessagesTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using FluentAssertions;
using Xunit;

namespace Akka.Hosting.TestKit.Tests.TestKitBaseTests;

public class IgnoreMessagesTests : TestKit
{
    public class IgnoredMessage
    {
        public IgnoredMessage(string? ignoreMe = null)
        {
            IgnoreMe = ignoreMe;
        }

        public string? IgnoreMe { get; }
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
    }

    [Fact]
    public void IgnoreMessages_should_ignore_messages()
    {
        IgnoreMessages(o => o is 1);
        TestActor.Tell(1);
        TestActor.Tell("1");
        string.Equals((string)ReceiveOne(), "1").Should().BeTrue();
        HasMessages.Should().BeFalse();
    }
        
    [Fact]
    public void IgnoreMessages_should_ignore_messages_T()
    {
        IgnoreMessages<IgnoredMessage>();
            
        TestActor.Tell("1");
        TestActor.Tell(new IgnoredMessage(), TestActor);
        TestActor.Tell("2");
        ReceiveN(2).Should().BeEquivalentTo(new[] { "1", "2" }, opt => opt.WithStrictOrdering());
        HasMessages.Should().BeFalse();
    }

    [Fact]
    public void IgnoreMessages_should_ignore_messages_T_with_Func()
    {
        IgnoreMessages<IgnoredMessage>(m => String.IsNullOrWhiteSpace(m.IgnoreMe));

        var msg = new IgnoredMessage("not ignored!");

        TestActor.Tell("1");
        TestActor.Tell(msg, TestActor);
        TestActor.Tell("2");
        ReceiveN(3).Should().BeEquivalentTo(new object[] { "1", msg, "2" }, opt => opt.WithStrictOrdering());
        HasMessages.Should().BeFalse();
    }
}