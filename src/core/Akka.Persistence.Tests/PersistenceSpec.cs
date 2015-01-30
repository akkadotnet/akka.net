using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Akka.Util.Internal;

namespace Akka.Persistence.Tests
{
    public abstract class PersistenceSpec : AkkaSpec
    {
        public static Config Configuration(string plugin, string test, string serialization = null,
            string extraConfig = null)
        {
            var c = extraConfig == null
                ? ConfigurationFactory.Empty
                : ConfigurationFactory.ParseString(extraConfig);
            var configString = string.Format(@"
                akka.actor.serialize-creators = {0}
                akka.actor.serialize-messages = {0}
                akka.persistence.publish-plugin-commands = on
                akka.persistence.journal.plugin = ""akka.persistence.journal.{1}""
                akka.persistence.snapshot-store.local.dir = ""target/snapshots-{2}/""
                akka.test.single-expect-default = 10s", serialization ?? "on", plugin, test);

            return c.WithFallback(ConfigurationFactory.ParseString(configString)).WithFallback(Persistence.DefaultConfig());
        }

        internal readonly Cleanup Clean;

        private readonly AtomicCounter _counter = new AtomicCounter(0);

        private readonly string _name;

        protected PersistenceSpec(string config)
            : base(config)
        {
            _name = NamePrefix + "-" + _counter.GetAndIncrement();
            Clean = new Cleanup(this);
            Clean.Initialize();
        }

        protected PersistenceSpec(Config config = null)
            : base(config)
        {
            _name = NamePrefix + "-" + _counter.GetAndIncrement();
            Clean = new Cleanup(this);
            Clean.Initialize();
        }

        public PersistenceExtension Extension { get { return Persistence.Instance.Apply(Sys); } }

        public string NamePrefix { get { return Sys.Name; } }
        public string Name { get { return _name; } }

        protected override void AfterTest()
        {
            base.AfterTest();
            Clean.Dispose();
        }
    }

    internal class Cleanup : IDisposable
    {
        internal List<DirectoryInfo> StorageLocations;

        public Cleanup(AkkaSpec spec)
        {
            StorageLocations = new[]
            {
                "akka.persistence.snapshot-store.local.dir"
            }.Select(s => new DirectoryInfo(spec.Sys.Settings.Config.GetString(s))).ToList();
        }

        public void Initialize()
        {
            StorageLocations.ForEach(fi =>
            {
                if (fi.Exists) fi.Delete(true);
            });
        }

        public void Dispose()
        {
            StorageLocations.ForEach(fi => fi.Delete(true));
        }
    }

    public abstract class NamedPersistentActor : PersistentActor
    {
        private readonly string _name;

        protected NamedPersistentActor(string name)
        {
            _name = name;
        }

        public override string PersistenceId
        {
            get { return _name; }
        }
    }

    internal sealed class GetState
    {
        public static readonly GetState Instance = new GetState();
        private GetState() { }
    }

    internal class TestException : Exception
    {
        public TestException(string message)
            : base(message)
        {
        }
    }
}