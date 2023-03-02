//-----------------------------------------------------------------------
// <copyright file="IgnoreMessagesTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Actor;
using Xunit;
using FluentAssertions;

namespace Akka.TestKit.Tests.TestKitBaseTests
{
    public class IgnoreMessagesTests : AkkaSpec
    {
        public class IgnoredMessage
        {
            public IgnoredMessage(string ignoreMe = null)
            {
                IgnoreMe = ignoreMe;
            }

            public string IgnoreMe { get; }
        }

        [Fact]
        public async Task IgnoreMessages_should_ignore_messages()
        {
            IgnoreMessages(o => o is int i && i == 1);
            TestActor.Tell(1);
            TestActor.Tell("1");
            (await ReceiveOneAsync()).Should().Be("1");
            HasMessages.Should().BeFalse();
        }
        
        [Fact]
        public async Task IgnoreMessages_should_ignore_messages_T()
        {
            IgnoreMessages<IgnoredMessage>();
            
            TestActor.Tell("1");
            TestActor.Tell(new IgnoredMessage(), TestActor);
            TestActor.Tell("2");
            await ReceiveNAsync(2).ShouldOnlyContainInOrderAsync("1", "2");
            HasMessages.Should().BeFalse();
        }

        [Fact]
        public async Task IgnoreMessages_should_ignore_messages_T_with_Func()
        {
            IgnoreMessages<IgnoredMessage>(m => string.IsNullOrWhiteSpace(m.IgnoreMe));

            var msg = new IgnoredMessage("not ignored!");

            TestActor.Tell("1");
            TestActor.Tell(msg, TestActor);
            TestActor.Tell("2");
            await ReceiveNAsync(3).ShouldOnlyContainInOrderAsync("1", msg, "2");
            HasMessages.Should().BeFalse();
        }
    }
}
