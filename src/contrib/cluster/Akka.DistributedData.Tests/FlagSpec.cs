//-----------------------------------------------------------------------
// <copyright file="FlagSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.DistributedData.Tests
{
    [Collection("DistributedDataSpec")]
    public class FlagSpec
    {
        public FlagSpec(ITestOutputHelper output)
        {
        }

        [Fact]
        public void A_Flag_should_be_able_to_switch_on_once()
        {
            var f1 = new Flag();
            var f2 = f1.SwitchOn();
            var f3 = f2.SwitchOn();

            f1.Enabled.Should().BeFalse();
            f2.Enabled.Should().BeTrue();
            f3.Enabled.Should().BeTrue();
        }

        [Fact]
        public void A_Flag_should_merge_by_pickling_true()
        {
            var f1 = new Flag(false);
            var f2 = f1.SwitchOn();

            var m1 = f1.Merge(f2);
            m1.Enabled.Should().BeTrue();

            var m2 = f2.Merge(f1);
            m2.Enabled.Should().BeTrue();
        }
    }
}
