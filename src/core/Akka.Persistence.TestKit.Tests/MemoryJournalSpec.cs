//-----------------------------------------------------------------------
// <copyright file="MemoryJournalSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Persistence.Journal;
using Akka.Persistence.TestKit.Journal;
using Xunit.Abstractions;

namespace Akka.Persistence.TestKit.Tests
{
    public class MemoryJournalSpec : JournalSpec
    {
        public MemoryJournalSpec(ITestOutputHelper output)
            : base(typeof(MemoryJournal), "MemoryJournalSpec", output)
        {
            Initialize();
        }

        protected override bool SupportsRejectingNonSerializableObjects { get { return false; } }
    }
}
