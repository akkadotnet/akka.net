//-----------------------------------------------------------------------
// <copyright file="PersistenceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Akka.Configuration;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit.Abstractions;

namespace Akka.Persistence.Tests
{
    public abstract class PersistenceSpec : AkkaSpec
    {
        public static Config Configuration(string test, string serialization = null,
            string extraConfig = null)
        {
            var c = extraConfig == null
                ? ConfigurationFactory.Empty
                : ConfigurationFactory.ParseString(extraConfig);
            var configString = string.Format(@"
                akka.actor.serialize-creators = {0}
                akka.actor.serialize-messages = {0}
                akka.persistence.publish-plugin-commands = on
                akka.persistence.snapshot-store.local.dir = ""target/snapshots-{1}/""
                akka.test.single-expect-default = 10s", serialization ?? "on", test);

            return c.WithFallback(ConfigurationFactory.ParseString(configString));
        }

        internal readonly Cleanup Clean;

        private readonly AtomicCounter _counter = new AtomicCounter(0);

        private readonly string _name;

        protected PersistenceSpec(string config, ITestOutputHelper output = null)
            : base(config, output)
        {
            _name = NamePrefix + "-" + _counter.GetAndIncrement();
            Clean = new Cleanup(this);
            Clean.Initialize();
        }

        protected PersistenceSpec(Config config = null, ITestOutputHelper output = null)
            : base(config, output)
        {
            _name = NamePrefix + "-" + _counter.GetAndIncrement();
            Clean = new Cleanup(this);
            Clean.Initialize();
        }

        public PersistenceExtension Extension { get { return Persistence.Instance.Apply(Sys); } }

        public string NamePrefix { get { return Sys.Name; } }
        public string Name { get { return _name; } }

        protected override void AfterAll()
        {
            base.AfterAll();
            Clean.Dispose();
        }

        protected void ExpectMsgInOrder(params object[] ordered)
        {
            var msg = ExpectMsg<object[]>();
            msg
                //.Select(x => x.ToString())
                .ShouldOnlyContainInOrder(ordered);
        }
    }

    internal class Cleanup : IDisposable
    {
        internal List<DirectoryInfo> StorageLocations;
        private static readonly object _syncRoot = new object();

        public Cleanup(AkkaSpec spec)
        {
            StorageLocations = new[]
            {
                "akka.persistence.snapshot-store.local.dir"
            }.Select(s => new DirectoryInfo(spec.Sys.Settings.Config.GetString(s))).ToList();
        }

        public void Initialize()
        {
            DeleteStorageLocations();
        }

        private void DeleteStorageLocations()
        {
            StorageLocations.ForEach(fi =>
            {
                lock (_syncRoot)
                {
                    try
                    {
                        if (fi.Exists) fi.Delete(true);    
                    }
                    catch (IOException) { }
                }
            });
        }

        public void Dispose()
        {
            DeleteStorageLocations();
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

