//-----------------------------------------------------------------------
// <copyright file="OffsetCompareSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

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

            compare1.ShouldThrow<InvalidOperationException>();
            compare2.ShouldThrow<InvalidOperationException>();
        }
    }
}
