//-----------------------------------------------------------------------
// <copyright file="OffsetSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Akka.Persistence.Query.Tests
{
    public class OffsetSpec
    {
        [Fact]
        public void TimeBasedUuid_offset_must_be_ordered_correctly()
        {
            var uuid1 = new TimeBasedUuid(TimeUuid.Parse("49225740-2019-11ea-a6f0-a0a60c2ef4ff")); //2019-12-16T15:32:36.148Z[UTC]            
            var uuid2 = new TimeBasedUuid(TimeUuid.Parse("91be23d0-2019-11ea-a752-ffae2393b6e4")); //2019-12-16T15:34:37.965Z[UTC]
            var uuid3 = new TimeBasedUuid(TimeUuid.Parse("91f95810-2019-11ea-a752-ffae2393b6e4")); //2019-12-16T15:34:38.353Z[UTC]

            ((TimeUuid)uuid1.Value).GetDate().Should().BeBefore(((TimeUuid)uuid2.Value).GetDate());
            ((TimeUuid)uuid2.Value).GetDate().Should().BeBefore(((TimeUuid)uuid3.Value).GetDate());

            new List<TimeBasedUuid>() { uuid2, uuid1, uuid3 }.OrderBy(_ => Guid.NewGuid())
                .Should().BeEquivalentTo(new List<TimeBasedUuid>() { uuid1, uuid2, uuid3 });
            new List<TimeBasedUuid>() { uuid3, uuid2, uuid1 }.OrderBy(_ => Guid.NewGuid())
                .Should().BeEquivalentTo(new List<TimeBasedUuid>() { uuid1, uuid2, uuid3 });
        }

        [Fact]
        public void Sequence_offset_must_be_ordered_correctly()
        {
            var sequenceBasedList = new List<long> { 1L, 2L, 3L }.Select(l => new Sequence(l));
            var shuffledSequenceBasedList = sequenceBasedList.OrderBy(_ => Guid.NewGuid());
            shuffledSequenceBasedList.Should().BeEquivalentTo(sequenceBasedList);
        }
    }
}
