//-----------------------------------------------------------------------
// <copyright file="SystemMessageListSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Dispatch.SysMsg;
using Akka.TestKit;
using Xunit;
using static Akka.Dispatch.SysMsg.SystemMessageList;

namespace Akka.Tests.Dispatch.SysMsg
{
    public class SystemMessageListSpec : AkkaSpec
    {
        [Fact]
        public void SystemMessageList_value_class_must_handle_empty_lists_correctly()
        {
            LNil.Head.ShouldBe(null);
            LNil.IsEmpty.ShouldBeTrue();
            (LNil.Reverse.Equals(ENil)).ShouldBeTrue();
        }

        [Fact]
        public void SystemMessageList_value_class_must_be_able_to_append_messages()
        {
            var create0 = new Failed(null, null, 0);
            var create1 = new Failed(null, null, 1);
            var create2 = new Failed(null, null, 2);
            ((LNil + create0).Head == create0).ShouldBeTrue();
            ((LNil + create0 + create1).Head == create1).ShouldBeTrue();
            ((LNil + create0 + create1 + create2).Head == create2).ShouldBeTrue();

            (create2.Next == create1).ShouldBeTrue();
            (create1.Next == create0).ShouldBeTrue();
            (create0.Next == null).ShouldBeTrue();
        }

        [Fact]
        public void SystemMessageList_value_class_must_be_able_to_deconstruct_head_and_tail()
        {
            var create0 = new Failed(null, null, 0);
            var create1 = new Failed(null, null, 1);
            var create2 = new Failed(null, null, 2);
            var list = LNil + create0 + create1 + create2;

            (list.Head == create2).ShouldBeTrue();
            (list.Tail.Head == create1).ShouldBeTrue();
            (list.Tail.Tail.Head == create0).ShouldBeTrue();
            (list.Tail.Tail.Tail.Head == null).ShouldBeTrue();
        }

        [Fact]
        public void SystemMessageList_value_class_must_be_able_to_properly_report_size_and_emptiness()
        {
            var create0 = new Failed(null, null, 0);
            var create1 = new Failed(null, null, 1);
            var create2 = new Failed(null, null, 2);
            var list = LNil + create0 + create1 + create2;

            list.Size.ShouldBe(3);
            list.IsEmpty.ShouldBeFalse();

            list.Tail.Size.ShouldBe(2);
            list.Tail.IsEmpty.ShouldBeFalse();

            list.Tail.Tail.Size.ShouldBe(1);
            list.Tail.Tail.IsEmpty.ShouldBeFalse();

            list.Tail.Tail.Tail.Size.ShouldBe(0);
            list.Tail.Tail.Tail.IsEmpty.ShouldBeTrue();
        }

        [Fact]
        public void SystemMessageList_value_class_must_be_able_to_properly_reverse_contents()
        {
            var create0 = new Failed(null, null, 0);
            var create1 = new Failed(null, null, 1);
            var create2 = new Failed(null, null, 2);
            var list = LNil + create0 + create1 + create2;
            EarliestFirstSystemMessageList listRev = list.Reverse;

            listRev.IsEmpty.ShouldBeFalse();
            listRev.Size.ShouldBe(3);

            (listRev.Head == create0).ShouldBeTrue();
            (listRev.Tail.Head == create1).ShouldBeTrue();
            (listRev.Tail.Tail.Head == create2).ShouldBeTrue();
            (listRev.Tail.Tail.Tail.Head == null).ShouldBeTrue();

            (create0.Next == create1).ShouldBeTrue();
            (create1.Next == create2).ShouldBeTrue();
            (create2.Next == null).ShouldBeTrue();
        }

        [Fact]
        public void EarliestFirstSystemMessageList_must_properly_prepend_reversed_message_lists_to_the_front()
        {
            var create0 = new Failed(null, null, 0);
            var create1 = new Failed(null, null, 1);
            var create2 = new Failed(null, null, 2);
            var create3 = new Failed(null, null, 3);
            var create4 = new Failed(null, null, 4);
            var create5 = new Failed(null, null, 5);

            var fwdList = ENil + create5 + create4 + create3;
            var revList = LNil + create0 + create1 + create2;

            var list = fwdList + revList;

            (list.Head == create0).ShouldBeTrue();
            (list.Tail.Head == create1).ShouldBeTrue();
            (list.Tail.Tail.Head == create2).ShouldBeTrue();
            (list.Tail.Tail.Tail.Head == create3).ShouldBeTrue();
            (list.Tail.Tail.Tail.Tail.Head == create4).ShouldBeTrue();
            (list.Tail.Tail.Tail.Tail.Tail.Head == create5).ShouldBeTrue();

            ((ENil + LNil).Equals(ENil)).ShouldBeTrue();
            (((ENil + create0) + LNil).Head == create0).ShouldBeTrue();
            ((ENil + (LNil + create0)).Head == create0).ShouldBeTrue();
        }
    }
}

