//-----------------------------------------------------------------------
// <copyright file="ExpectTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using FluentAssertions;
using Xunit;
using Xunit.Sdk;
using static FluentAssertions.FluentActions;

namespace Akka.TestKit.Tests.TestKitBaseTests
{
    public class ExpectTests : AkkaSpec
    {
        [Fact]
        public async Task ExpectMsgAllOfAsync_should_receive_correct_messages()
        {
            TestActor.Tell("1");
            TestActor.Tell("2");
            TestActor.Tell("3");
            TestActor.Tell("4");
            await ExpectMsgAllOfAsync(new []{ "3", "1", "4", "2"})
                .ShouldOnlyContainInOrderAsync("1", "2", "3", "4");
        }

        [Fact]
        public async Task ExpectMsgAllOfAsync_should_fail_when_receiving_unexpected()
        {
            TestActor.Tell("1");
            TestActor.Tell("2");
            TestActor.Tell("Totally unexpected");
            TestActor.Tell("3");
            await Awaiting(async () =>
            {
                await ExpectMsgAllOfAsync(new[] { "3", "1", "2" }).ToListAsync();
            }).Should().ThrowAsync<XunitException>().WithMessage("not found [*");
        }

        [Fact]
        public async Task ExpectMsgAllOfAsync_should_timeout_when_not_receiving_any_messages()
        {
            await Awaiting(async () =>
            {
                await ExpectMsgAllOfAsync(TimeSpan.FromMilliseconds(100), new[] { "3", "1", "2" }).ToListAsync();
            }).Should().ThrowAsync<XunitException>().WithMessage("Timeout (*");
        }

        [Fact]
        public async Task ExpectMsgAllOfAsync_should_timeout_if_to_few_messages()
        {
            TestActor.Tell("1");
            TestActor.Tell("2");
            await Awaiting(async () =>
            {
                await ExpectMsgAllOfAsync(TimeSpan.FromMilliseconds(100), new[] { "3", "1", "2" }).ToListAsync();
            }).Should().ThrowAsync<XunitException>().WithMessage("Timeout (*");
        }
        
        // Tests for "ExpectMsgAllOfMatchingPredicates" methods
        [Fact]
        public void ExpectMsgAllOfMatchingPredicatesAsync_should_receive_correct_messages()
        {
            TestActor.Tell("1");
            TestActor.Tell("2");
            TestActor.Tell("3");
            TestActor.Tell("4");
            ExpectMsgAllOfMatchingPredicates(new[]
            {
                PredicateInfo.Create<string>(s => s.Equals("3")), 
                PredicateInfo.Create<string>(s => s.Equals("1")),
                PredicateInfo.Create<string>(s => s.Equals("4")), 
                PredicateInfo.Create<string>(s => s.Equals("2"))
            });
        }
        
        [Fact]
        public void ExpectMsgAllOfMatchingPredicatesAsync_with_mixed_message_types_should_receive_correct_messages()
        {
            TestActor.Tell(1);
            TestActor.Tell("2");
            TestActor.Tell(3);
            TestActor.Tell("4");
            ExpectMsgAllOfMatchingPredicates(new[]
            {
                PredicateInfo.Create<int>(s => s.Equals(3)),         // type `int`
                PredicateInfo.Create<int>(s => s.Equals(1)),         // type `int`
                PredicateInfo.Create<string>(s => s.Equals("4")),     // type `string`
                PredicateInfo.Create<string>(s => s.Equals("2"))      // type `string`
            });
        }
        
        [Fact]
        public async Task ExpectMsgAllOfMatchingPredicatesAsync_with_mixed_message_types_should_receive_correct_messages_in_order()
        {
            TestActor.Tell(1);
            TestActor.Tell("2");
            TestActor.Tell(3);
            TestActor.Tell("4");
            await ExpectMsgAllOfMatchingPredicatesAsync(new[]
            {
                PredicateInfo.Create<int>(s => s.Equals(3)),         // type `int`
                PredicateInfo.Create<int>(s => s.Equals(1)),         // type `int`
                PredicateInfo.Create<string>(s => s.Equals("4")),     // type `string`
                PredicateInfo.Create<string>(s => s.Equals("2"))      // type `string`
            })
            .ShouldOnlyContainInOrderAsync(1, "2",3, "4");
        } 
        
        [Fact]
        public async Task ExpectMsgAllOfMatchingPredicatesAsync_should_fail_when_receiving_unexpected()
        {
            TestActor.Tell(1);
            TestActor.Tell("2");
            TestActor.Tell("Totally unexpected");
            TestActor.Tell(3);
            await Awaiting(async () =>
            {
                await ExpectMsgAllOfMatchingPredicatesAsync(new []
                {   
                    new Predicate<int>(s => s == 3).CreatePredicateInfo(),
                    new Predicate<int>(s => s == 1).CreatePredicateInfo(),
                    new Predicate<string>(s => s.Equals("4")).CreatePredicateInfo(),
                    new Predicate<string>(s => s.Equals("2")).CreatePredicateInfo()
                }).ToListAsync();
            }).Should().ThrowAsync<XunitException>().WithMessage("not found [*");
        }
        
        [Fact]
        public async Task ExpectMsgAllOfMatchingPredicatesAsync_should_timeout_when_not_receiving_any_messages()
        {
            await Awaiting(async () =>
            {
                await ExpectMsgAllOfMatchingPredicatesAsync(TimeSpan.FromMilliseconds(100), new []
                {
                    new Predicate<int>(s => s.Equals(3)).CreatePredicateInfo(),
                    new Predicate<int>(s => s.Equals(1)).CreatePredicateInfo(),
                    new Predicate<string>(s => s.Equals("2")).CreatePredicateInfo()
                }).ToListAsync();
            }).Should().ThrowAsync<XunitException>().WithMessage("Timeout (*");
        }
        
        [Fact]
        public async Task ExpectMsgAllOfMatchingPredicatesAsync_should_timeout_if_to_few_messages()
        {
            TestActor.Tell("1");
            TestActor.Tell("2");
            await Awaiting(async () =>
            {
                await ExpectMsgAllOfMatchingPredicatesAsync(TimeSpan.FromMilliseconds(100), new []
                    {
                        new Predicate<int>(s => s.Equals(3)).CreatePredicateInfo(),
                        new Predicate<int>(s => s.Equals(1)).CreatePredicateInfo(),
                        new Predicate<string>(s => s.Equals("2")).CreatePredicateInfo(),
                    })
                    .ToListAsync();
            }).Should().ThrowAsync<XunitException>().WithMessage("Timeout (*");
        }
    }
}
