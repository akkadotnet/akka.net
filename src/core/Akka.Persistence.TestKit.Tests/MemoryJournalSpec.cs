//-----------------------------------------------------------------------
// <copyright file="MemoryJournalSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Persistence.TestKit.Journal;

namespace Akka.Persistence.TestKit.Tests
{
    public class MemoryJournalSpec : JournalSpec
    {
        public MemoryJournalSpec()
            : base(actorSystemName: "MemoryJournalSpec")
        {
        }
    }
}
