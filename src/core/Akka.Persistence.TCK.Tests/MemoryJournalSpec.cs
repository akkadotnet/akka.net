//-----------------------------------------------------------------------
// <copyright file="MemoryJournalSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Persistence.Journal;
using Akka.Persistence.TCK.Journal;
using Xunit.Abstractions;

namespace Akka.Persistence.TCK.Tests
{
    public class MemoryJournalSpec : JournalSpec
    {
        public MemoryJournalSpec(ITestOutputHelper output)
            : base(typeof(MemoryJournal), "MemoryJournalSpec", output)
        {
            Initialize();
        }

        protected override bool SupportsRejectingNonSerializableObjects { get { return false; } }

        protected override bool SupportsSerialization => true;
    }
}
