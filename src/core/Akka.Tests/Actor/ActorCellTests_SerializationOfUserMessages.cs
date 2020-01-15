//-----------------------------------------------------------------------
// <copyright file="ActorCellTests_SerializationOfUserMessages.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.TestKit;
using Akka.Tests.TestUtils;
using Xunit;

namespace Akka.Tests.Actor
{
    public class WhenSerializeAllMessagesIsOff : AkkaSpec
    {
        public class SomeUserMessage : Comparable
        {
            public string A { get; set; }
            public int B { get; set; }
            public Guid C { get; set; }
        }

        public WhenSerializeAllMessagesIsOff()
            : base(@"akka.actor.serialize-messages = off")
        {
        }

       [Fact]
       public void Does_not_serializes_user_messages()
       {
            var message = new SomeUserMessage
            {
                A = "abc",
                B = 123,
                C = Guid.Empty
            };
            TestActor.Tell(message);

            var result = ExpectMsg<SomeUserMessage>();

            Assert.False(Sys.Settings.SerializeAllMessages);
            Assert.Equal(message, result);
            Assert.Same(message, result);
        }

    }

    public class WhenSerializeAllMessagesIsOn : AkkaSpec
    {
        public class SomeUserMessage : Comparable
        {
            public string A { get; set; }
            public int B { get; set; }
            public Guid C { get; set; }
        }

        public WhenSerializeAllMessagesIsOn():base(@"akka.actor.serialize-messages = on")
        {
        }
       
        [Fact]
        public void Do_serialize_user_messages()
        {
            var message = new SomeUserMessage
            {
                A = "abc",
                B = 123,
                C = Guid.Empty
            };
            TestActor.Tell(message);

            var result = ExpectMsg<SomeUserMessage>();

            Assert.True(Sys.Settings.SerializeAllMessages);
            Assert.Equal(message, result);
            Assert.NotSame(message, result);
        }
    }
}

