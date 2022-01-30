//-----------------------------------------------------------------------
// <copyright file="ReceiveTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Sdk;

namespace Akka.Testkit.Tests.TestKitBaseTests
{
    public class ReceiveTests : AkkaSpec
    {
        [Fact]
        public void ReceiveN_should_receive_correct_number_of_messages()
        {
            TestActor.Tell("1");
            TestActor.Tell("2");
            TestActor.Tell("3");
            TestActor.Tell("4");
            ReceiveN(3).ShouldOnlyContainInOrder("1", "2", "3");
            ReceiveN(1).ShouldOnlyContainInOrder("4");
        }

        [Fact]
        public void ReceiveN_should_timeout_if_no_messages()
        {
            Intercept(() => ReceiveN(3, TimeSpan.FromMilliseconds(10)));
        }

        [Fact]
        public void ReceiveN_should_timeout_if_to_few_messages()
        {
            TestActor.Tell("1");
            TestActor.Tell("2");
            Intercept(() => ReceiveN(3, TimeSpan.FromMilliseconds(100)));
        }


        [Fact]
        public void FishForMessage_should_return_matched_message()
        {
            TestActor.Tell(1);
            TestActor.Tell(2);
            TestActor.Tell(10);
            TestActor.Tell(20);
            FishForMessage<int>(i => i >= 10).ShouldBe(10);
        }

        [Fact]
        public void FishForMessage_should_timeout_if_no_messages()
        {
            Intercept(() => FishForMessage(_ => false, TimeSpan.FromMilliseconds(10)));
        }

        [Fact]
        public void FishForMessage_should_timeout_if_to_few_messages()
        {
            TestActor.Tell("1");
            TestActor.Tell("2");
            Intercept(() => FishForMessage(_ => false, TimeSpan.FromMilliseconds(100)));
        }

        [Fact]
        public async Task FishForMessage_should_fill_the_all_messages_param_if_not_null()
        {
            await Task.Run(delegate
            {
                var probe = base.CreateTestProbe("probe");
                probe.Tell("hello");
                probe.Tell("world");
                var allMessages = new List<object>();
                probe.FishForMessage<string>(isMessage: s => s == "world", allMessages: allMessages);
                allMessages.Should().BeEquivalentTo(new List<object> { "hello" });
            });
        }

        [Fact]
        public async Task FishUntilMessageAsync_should_succeed_with_good_input()
        {
            var probe = CreateTestProbe("probe");
            probe.Ref.Tell(1d, TestActor);
            await probe.FishUntilMessageAsync<int>(max: TimeSpan.FromMilliseconds(10));
        }


        [Fact]
        public async Task FishUntilMessageAsync_should_fail_with_bad_input()
        {
            var probe = CreateTestProbe("probe");
            probe.Ref.Tell(3, TestActor);
            Func<Task> func = () => probe.FishUntilMessageAsync<int>(max: TimeSpan.FromMilliseconds(10));
            await func.Should().ThrowAsync<Exception>();
        }

        [Fact]
        public void ReceiveWhile_Filter_should_on_a_timeout_return_no_messages()
        {
            ReceiveWhile<object>(_ => _, TimeSpan.FromMilliseconds(10)).Count.ShouldBe(0);
        }

        [Fact]
        public void ReceiveWhile_Filter_should_break_on_function_returning_null_and_return_correct_messages()
        {
            TestActor.Tell("1");
            TestActor.Tell(2);
            TestActor.Tell("3");
            TestActor.Tell(99999.0);
            TestActor.Tell(4);
            ReceiveWhile<string>(_ => _ is double ? null : _.ToString()).ShouldOnlyContainInOrder("1", "2", "3");
        }

        [Fact]
        public void ReceiveWhile_Filter_should_not_consume_last_message_that_didnt_match()
        {
            TestActor.Tell("1");
            TestActor.Tell("2");
            TestActor.Tell(4711);
            ReceiveWhile<object>(_ => _ is string ? _ : null);
            ExpectMsg(4711);
        }

        [Fact]
        public void ReceiveWhile_Predicate_should_on_a_timeout_return_no_messages()
        {
            ReceiveWhile<object>(_ => false, TimeSpan.FromMilliseconds(10)).Count.ShouldBe(0);
        }

        [Fact]
        public void ReceiveWhile_Predicate_should_break_when_predicate_returns_false_and_return_correct_messages()
        {
            TestActor.Tell("1");
            TestActor.Tell("2");
            TestActor.Tell("3");
            TestActor.Tell("-----------");
            TestActor.Tell("4");
            ReceiveWhile<string>(s => s.Length == 1).ShouldOnlyContainInOrder("1", "2", "3");
        }

        [Fact]
        public void
            ReceiveWhile_Predicate_should_break_when_type_is_wrong_and_we_dont_ignore_those_and_return_correct_messages()
        {
            TestActor.Tell("1");
            TestActor.Tell("2");
            TestActor.Tell("3");
            TestActor.Tell(4);
            TestActor.Tell("5");
            ReceiveWhile<string>(s => s.Length == 1, shouldIgnoreOtherMessageTypes: false)
                .ShouldOnlyContainInOrder("1", "2", "3");
        }

        [Fact]
        public void
            ReceiveWhile_Predicate_should_continue_when_type_is_other_but_we_ignore_other_types_and_return_correct_messages()
        {
            TestActor.Tell("1");
            TestActor.Tell("2");
            TestActor.Tell("3");
            TestActor.Tell(4);
            TestActor.Tell("5");
            ReceiveWhile<string>(s => s.Length == 1, shouldIgnoreOtherMessageTypes: true)
                .ShouldOnlyContainInOrder("1", "2", "3", "5");
        }

        [Fact]
        public void ReceiveWhile_Predicate_should_not_consume_last_message_that_didnt_match()
        {
            TestActor.Tell("1");
            TestActor.Tell("2");
            TestActor.Tell(4711);
            TestActor.Tell("3");
            TestActor.Tell("4");
            TestActor.Tell("5");
            TestActor.Tell(6);
            TestActor.Tell("7");
            TestActor.Tell("8");

            var received = ReceiveWhile<object>(_ => _ is string);
            received.ShouldOnlyContainInOrder("1", "2");

            ExpectMsg(4711);

            received = ReceiveWhile<object>(_ => _ is string);
            received.ShouldOnlyContainInOrder("3", "4", "5");

            ExpectMsg(6);
        }
    }
}