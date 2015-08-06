//-----------------------------------------------------------------------
// <copyright file="TestKitTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//----------------------------------------------------------------------


using Akka.Actor;
using Machine.Specifications;

namespace Akka.TestKit.MSpec.Tests
{
    [Subject(typeof(TestKit))]
    class When_a_message_is_send_to_the_test_actor : TestKit
    {
        private const string Message = "Message";
        
        private Because of = () => Kit.TestActor.Tell(Message);
        
        private It should_receive_the_message = () => Kit.ExpectMsg(Message);
    }
}