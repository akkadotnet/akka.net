//-----------------------------------------------------------------------
// <copyright file="TestProbeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using Akka.Actor;
using Xunit;

namespace Akka.TestKit.Tests.TestActorRefTests
{
    public class TestProbeSpec : AkkaSpec
    {
        [Fact]
        public void TestProbe_should_equal_underlying_Ref()
        {
            var p = CreateTestProbe();
            p.Equals(p.Ref).ShouldBeTrue();
            p.Ref.Equals(p).ShouldBeTrue();
            var hs = new HashSet<IActorRef> {p, p.Ref};
            hs.Count.ShouldBe(1);
        }

        /// <summary>
        /// Should be able to receive a <see cref="Terminated"/> message from a <see cref="TestProbe"/>
        /// if we're deathwatching it and it terminates.
        /// </summary>
        [Fact]
        public void TestProbe_should_send_Terminated_when_killed()
        {
            var p = CreateTestProbe();
            Watch(p);
            Sys.Stop(p);
            ExpectTerminated(p);
        }

        /// <summary>
        /// If we deathwatch the underlying actor ref or TestProbe itself, it shouldn't matter.
        /// 
        /// They should be equivalent either way.
        /// </summary>
        [Fact]
        public void TestProbe_underlying_Ref_should_be_equivalent_to_TestProbe()
        {
            var p = CreateTestProbe();
            Watch(p.Ref);
            Sys.Stop(p);
            ExpectTerminated(p);
        }

        /// <summary>
        /// Should be able to receive a <see cref="Terminated"/> message from a <see cref="TestProbe.Ref"/>
        /// if we're deathwatching it and it terminates.
        /// </summary>
        [Fact]
        public void TestProbe_underlying_Ref_should_send_Terminated_when_killed()
        {
            var p = CreateTestProbe();
            Watch(p.Ref);
            Sys.Stop(p.Ref);
            ExpectTerminated(p.Ref);
        }
    }
}
