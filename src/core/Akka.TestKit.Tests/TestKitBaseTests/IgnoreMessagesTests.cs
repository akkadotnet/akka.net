//-----------------------------------------------------------------------
// <copyright file="IgnoreMessagesTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.TestKit;
using Xunit;

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
        public void IgnoreMessages_should_ignore_messages()
        {
            IgnoreMessages(o => o is int && (int)o == 1);
            TestActor.Tell(1);
            TestActor.Tell("1");
            String.Equals((string)ReceiveOne(), "1").ShouldBeTrue();
            HasMessages.ShouldBeFalse();
        }
        
        [Fact]
        public void IgnoreMessages_should_ignore_messages_T()
        {
            IgnoreMessages<IgnoredMessage>();
            
            TestActor.Tell("1");
            TestActor.Tell(new IgnoredMessage(), TestActor);
            TestActor.Tell("2");
            ReceiveN(2).ShouldOnlyContainInOrder("1", "2");
            HasMessages.ShouldBeFalse();
        }

        [Fact]
        public void IgnoreMessages_should_ignore_messages_T_with_Func()
        {
            IgnoreMessages<IgnoredMessage>(m => String.IsNullOrWhiteSpace(m.IgnoreMe));

            var msg = new IgnoredMessage("not ignored!");

            TestActor.Tell("1");
            TestActor.Tell(msg, TestActor);
            TestActor.Tell("2");
            ReceiveN(3).ShouldOnlyContainInOrder("1", msg, "2");
            HasMessages.ShouldBeFalse();
        }
    }
}
