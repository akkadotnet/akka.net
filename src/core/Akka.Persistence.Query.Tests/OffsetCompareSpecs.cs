//-----------------------------------------------------------------------
// <copyright file="OffsetCompareSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

#nullable enable
namespace Akka.Persistence.Query.Tests
{
    public class OffsetCompareSpecs
    {
        [Theory]
        [InlineData(new[]{ 1L, 3L, 4L, 5L, 6L })]
        [InlineData(new[] { 6L, 2L, 1L, 5L, 3L })]
        public void Offsets_should_compare_correctly(long[] seqNos)
        {
            var offsets = seqNos.Select(x => new Sequence(x)).Cast<Offset>();
            var orderedSeqNos = seqNos.OrderBy(x => x).ToList();
            var orderedOffset = new SortedSet<Offset>(offsets);

            var i = 0;
            foreach (var offset in orderedOffset.Cast<Sequence>())
            {
                offset.Value.Should().Be(orderedSeqNos[i]);
                i++;
            }
        }

        [Fact]
        public void Offsets_of_different_types_should_throw_on_compare()
        {
            Offset seq = new Sequence(0L);

            Action compare1 = () => seq.CompareTo(NoOffset.Instance);
            Action compare2 = () => NoOffset.Instance.CompareTo(seq);

            compare1.Should().Throw<InvalidOperationException>();
            compare2.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void FromEnd_offset_should_throw_on_compare()
        {
            // FromEnd is a relative, input-only offset with no position in the stream, so it cannot be
            // ordered against anything, including another FromEnd.
            Offset fromEnd = new FromEnd(5);

            Action compareToFromEnd = () => fromEnd.CompareTo(new FromEnd(5));
            Action compareToSequence = () => fromEnd.CompareTo(new Sequence(1L));
            Action sequenceCompareToFromEnd = () => new Sequence(1L).CompareTo(fromEnd);

            compareToFromEnd.Should().Throw<InvalidOperationException>();
            compareToSequence.Should().Throw<InvalidOperationException>();
            sequenceCompareToFromEnd.Should().Throw<InvalidOperationException>();
        }
    }
}
