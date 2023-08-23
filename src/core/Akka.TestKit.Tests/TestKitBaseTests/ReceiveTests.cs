//-----------------------------------------------------------------------
// <copyright file="ReceiveTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using FluentAssertions;
using Xunit;
using Xunit.Sdk;
using static FluentAssertions.FluentActions;

namespace Akka.TestKit.Tests.TestKitBaseTests
{
    public class ReceiveTests : AkkaSpec
    {
        [Fact]
        public async Task ReceiveNAsync_should_receive_correct_number_of_messages()
        {
            TestActor.Tell("1");
            TestActor.Tell("2");
            TestActor.Tell("3");
            TestActor.Tell("4");
            await ReceiveNAsync(3).ShouldOnlyContainInOrderAsync("1", "2", "3");
            await ReceiveNAsync(1).ShouldOnlyContainInOrderAsync("4");
        }

        [Fact]
        public async Task ReceiveNAsync_should_timeout_if_no_messages()
        {
            await Awaiting(async () => await ReceiveNAsync(3, TimeSpan.FromMilliseconds(10)).ToListAsync())
                .Should().ThrowAsync<Exception>();
        }

        [Fact]
        public async Task ReceiveNAsync_should_timeout_if_to_few_messages()
        {
            TestActor.Tell("1");
            TestActor.Tell("2");
            await Awaiting(async () => await ReceiveNAsync(3, TimeSpan.FromMilliseconds(100)).ToListAsync())
                .Should().ThrowAsync<Exception>();
        }

        [Fact]
        public async Task FishForMessageAsync_should_return_matched_message()
        {
            TestActor.Tell(1);
            TestActor.Tell(2);
            TestActor.Tell(10);
            TestActor.Tell(20);
            await FishForMessageAsync<int>(i => i >= 10).ShouldBeAsync(10);
        }

        [Fact]
        public async Task FishForMessageAsync_should_timeout_if_no_messages()
        {
            await Awaiting(async () => await FishForMessageAsync(_ => false, TimeSpan.FromMilliseconds(10)))
                .Should().ThrowAsync<Exception>();
        }

        [Fact]
        public async Task FishForMessageAsync_should_timeout_if_too_few_messages()
        {
            TestActor.Tell("1");
            TestActor.Tell("2");
            await Awaiting(async () => await FishForMessageAsync(_ => false, TimeSpan.FromMilliseconds(100)))
                .Should().ThrowAsync<Exception>();
        }

        [Fact]
        public async Task FishForMessageAsync_should_fill_the_all_messages_param_if_not_null()
        {
            var probe = CreateTestProbe("probe");
            probe.Tell("1");
            probe.Tell(2);
            probe.Tell("3");
            probe.Tell(4);
            var allMessages = new ArrayList();
            await probe.FishForMessageAsync<string>(isMessage: s => s == "3", allMessages: allMessages);
            allMessages.Should().BeEquivalentTo(new ArrayList { "1", 2 });
        }

        [Fact]
        public async Task FishForMessageAsync_should_clear_the_all_messages_param_if_not_null_before_filling_it()
        {
            var probe = CreateTestProbe("probe");
            probe.Tell("1");
            probe.Tell(2);
            probe.Tell("3");
            probe.Tell(4);
            var allMessages = new ArrayList { "pre filled data" };
            await probe.FishForMessageAsync<string>(isMessage: x => x == "3", allMessages: allMessages);
            allMessages.Should().BeEquivalentTo(new ArrayList { "1", 2 });
        }

        [Fact]
        public async Task FishUntilMessageAsync_should_succeed_with_good_input()
        {
            var probe = CreateTestProbe("probe");
            probe.Ref.Tell(1d, TestActor);
            await Awaiting(() => probe.FishUntilMessageAsync<int>(max: TimeSpan.FromMilliseconds(10)))
                .Should().NotThrowAsync();
        }

        [Fact]
        public async Task FishUntilMessageAsync_should_fail_with_bad_input()
        {
            var probe = CreateTestProbe("probe");
            probe.Ref.Tell(3, TestActor);
            await Awaiting(() => probe.FishUntilMessageAsync<int>(max: TimeSpan.FromMilliseconds(10)))
                .Should().ThrowAsync<Exception>();
        }

        [Fact]
        public async Task WaitForRadioSilenceAsync_should_succeed_immediately_with_null_good_input()
        {
            var probe = CreateTestProbe("probe");
            var messages = await probe.WaitForRadioSilenceAsync(max: TimeSpan.FromMilliseconds(0));
            messages.Should().BeEquivalentTo(new ArrayList());
        }

        [Fact]
        public async Task WaitForRadioSilenceAsync_should_succeed_immediately_with_good_pre_input()
        {
            var probe = CreateTestProbe("probe");
            probe.Ref.Tell(1, TestActor);
            var messages = await probe.WaitForRadioSilenceAsync(max: TimeSpan.FromMilliseconds(0));
            messages.Should().BeEquivalentTo(new ArrayList { 1 });
        }

        [Fact]
        public async Task WaitForRadioSilenceAsync_should_succeed_later_with_good_post_input()
        {
            var probe = CreateTestProbe("probe");
            var task = probe.WaitForRadioSilenceAsync();
            probe.Ref.Tell(1, TestActor);
            var messages = await task;
            messages.Should().BeEquivalentTo(new ArrayList { 1 });
        }

        [Fact]
        public async Task WaitForRadioSilenceAsync_should_reset_timer_twice_only()
        {
            var probe = CreateTestProbe("probe");
            var max = TimeSpan.FromMilliseconds(3000);
            var halfMax = TimeSpan.FromMilliseconds(max.TotalMilliseconds / 2);
            var doubleMax = TimeSpan.FromMilliseconds(max.TotalMilliseconds * 2);
            var task = probe.WaitForRadioSilenceAsync(max: max, maxMessages: 2);
            await Task.Delay(halfMax);
            probe.Ref.Tell(1, TestActor);
            await Task.Delay(halfMax);
            probe.Ref.Tell(2, TestActor);
            await Task.Delay(doubleMax);
            probe.Ref.Tell(3, TestActor);
            var messages = await task;
            messages.Should().BeEquivalentTo(new ArrayList { 1, 2 });
        }

        [Fact]
        public async Task WaitForRadioSilenceAsync_should_fail_immediately_with_bad_input()
        {
            var probe = CreateTestProbe("probe");
            probe.Ref.Tell(3, TestActor);
            await Awaiting(() => probe.WaitForRadioSilenceAsync(max: TimeSpan.FromMilliseconds(0), maxMessages: 0))
                .Should().ThrowAsync<FalseException>().WithMessage("maxMessages violated*");
        }

        [Fact]
        public async Task ReceiveWhileAsync_Filter_should_on_a_timeout_return_no_messages()
        {
            await using var e = ReceiveWhileAsync(_ => _, TimeSpan.FromMilliseconds(10)).GetAsyncEnumerator();
            (await e.MoveNextAsync()).Should().BeFalse();
        }

        [Fact]
        public async Task ReceiveWhileAsync_Filter_should_break_on_function_returning_null_and_return_correct_messages()
        {
            TestActor.Tell("1");
            TestActor.Tell(2);
            TestActor.Tell("3");
            TestActor.Tell(99999.0);
            TestActor.Tell(4);
            await ReceiveWhileAsync(_ => _ is double ? null : _.ToString()).ShouldOnlyContainInOrderAsync("1", "2", "3");
        }

        [Fact]
        public async Task ReceiveWhileAsync_Filter_should_not_consume_last_message_that_didnt_match()
        {
            TestActor.Tell("1");
            TestActor.Tell("2");
            TestActor.Tell(4711);
            await ReceiveWhileAsync(_ => _ is string ? _ : null).ToListAsync();
            await ExpectMsgAsync(4711);
        }

        [Fact]
        public async Task ReceiveWhileAsync_Predicate_should_on_a_timeout_return_no_messages()
        {
            await using var e = ReceiveWhileAsync<object>(_ => false, TimeSpan.FromMilliseconds(10)).GetAsyncEnumerator();
            (await e.MoveNextAsync()).Should().BeFalse();
        }

        [Fact]
        public async Task ReceiveWhileAsync_Predicate_should_break_when_predicate_returns_false_and_return_correct_messages()
        {
            TestActor.Tell("1");
            TestActor.Tell("2");
            TestActor.Tell("3");
            TestActor.Tell("-----------");
            TestActor.Tell("4");
            await ReceiveWhileAsync<string>(s => s.Length == 1).ShouldOnlyContainInOrderAsync("1", "2", "3");
        }

        [Fact]
        public async Task ReceiveWhileAsync_Predicate_should_break_when_type_is_wrong_and_we_dont_ignore_those_and_return_correct_messages()
        {
            TestActor.Tell("1");
            TestActor.Tell("2");
            TestActor.Tell("3");
            TestActor.Tell(4);
            TestActor.Tell("5");
            await ReceiveWhileAsync<string>(s => s.Length == 1, shouldIgnoreOtherMessageTypes: false)
                .ShouldOnlyContainInOrderAsync("1", "2", "3");
        }

        [Fact]
        public async Task ReceiveWhileAsync_Predicate_should_continue_when_type_is_other_but_we_ignore_other_types_and_return_correct_messages()
        {
            TestActor.Tell("1");
            TestActor.Tell("2");
            TestActor.Tell("3");
            TestActor.Tell(4);
            TestActor.Tell("5");
            await ReceiveWhileAsync<string>(s => s.Length == 1, shouldIgnoreOtherMessageTypes: true)
                .ShouldOnlyContainInOrderAsync("1", "2", "3", "5");
        }

        [Fact]
        public async Task ReceiveWhileAsync_Predicate_should_not_consume_last_message_that_didnt_match()
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

            await ReceiveWhileAsync<object>(_ => _ is string).ShouldOnlyContainInOrderAsync("1", "2");
            await ExpectMsgAsync(4711);
            await ReceiveWhileAsync<object>(_ => _ is string).ShouldOnlyContainInOrderAsync("3", "4", "5");
            await ExpectMsgAsync(6);
        }
    }
}
