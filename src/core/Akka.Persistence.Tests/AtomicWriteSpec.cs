//-----------------------------------------------------------------------
// <copyright file="AtomicWriteSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.TestKit;
using Xunit;

namespace Akka.Persistence.Tests
{
    public class AtomicWriteSpec
    {
        [Fact]
        public void AtomicWrite_must_only_contain_messages_for_the_same_persitence_id()
        {
            new AtomicWrite(ImmutableList.Create<IPersistentRepresentation>(
                    new Persistent("", 1, "p1"),
                    new Persistent("", 2, "p1")
                    )).PersistenceId.ShouldBe("p1");

            Assert.Throws<ArgumentException>(() =>
                new AtomicWrite(ImmutableList.Create<IPersistentRepresentation>(
                    new Persistent("", 1, "p1"),
                    new Persistent("", 2, "p1"),
                    new Persistent("", 3, "p2")))
                );
        }

        [Fact]
        public void AtomicWrite_must_have_correct_HighestSequenceNr()
        {
            new AtomicWrite(ImmutableList.Create<IPersistentRepresentation>(
                    new Persistent("", 1, "p1"),
                    new Persistent("", 2, "p1"),
                    new Persistent("", 3, "p1")
                    )).HighestSequenceNr.ShouldBe(3);
        }

        [Fact]
        public void AtomicWrite_must_have_correct_LowestSequenceNr()
        {
            new AtomicWrite(ImmutableList.Create<IPersistentRepresentation>(
                    new Persistent("", 2, "p1"),
                    new Persistent("", 3, "p1"),
                    new Persistent("", 4, "p1")
                    )).LowestSequenceNr.ShouldBe(2);
        }
    }
}