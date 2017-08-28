using System;
using Akka.Configuration;
using Akka.Persistence.TCK.Journal;
using Akka.Persistence.TestKit.Tests;
using Xunit.Abstractions;

namespace Akka.Persistence.TCK.Tests
{
    public class LocalJournalSpec: JournalSpec
    {
        private readonly string _path;

        protected override bool SupportsRejectingNonSerializableObjects {
            get { return false; }
        }

        public LocalJournalSpec(ITestOutputHelper output) : base(ConfigurationFactory.ParseString($@"
            akka.test.timefactor = 3
            akka.persistence.journal.plugin = ""akka.persistence.journal.local""
            akka.persistence.journal.local.dir = ""target/journals-{Guid.NewGuid()}"""), "LocalJournalSpec", output)
        {
            _path = Sys.Settings.Config.GetString("akka.persistence.journal.local.dir");
            Sys.CreateStorageLocations(_path);
            Initialize();
        }
        
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            Sys.DeleteStorageLocations(_path);
        }
    }
}