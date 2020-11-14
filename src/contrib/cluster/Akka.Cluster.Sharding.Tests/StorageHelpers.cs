//-----------------------------------------------------------------------
// <copyright file="StorageHelpers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests
{
    public static class StorageHelpers
    {
        public static void StartPersistence(this TestKitBase test, ActorSystem sys)
        {
            StartJournalPersistence(test, sys);
            StartSnapshotPersistence(test, sys);

            SetStore(test, sys, sys);
        }

        public static void SetStore(this TestKitBase test, ActorSystem startOn, ActorSystem storeOn)
        {
            SetJournalStore(test, startOn, storeOn);
            SetSnapshotStore(test, startOn, storeOn);
        }

        private static void StartJournalPersistence(this TestKitBase test, ActorSystem sys)
        {
            sys.Log.Info("Setting up setup shared journal.");
            Persistence.Persistence.Instance.Apply(sys).JournalFor("akka.persistence.journal.inmem");
        }

        private static void StartSnapshotPersistence(this TestKitBase test, ActorSystem sys)
        {
            sys.Log.Info("Setting up setup shared snapshot store.");
            Persistence.Persistence.Instance.Apply(sys).SnapshotStoreFor("akka.persistence.snapshot-store.inmem");
        }

        public static void SetJournalStore(this TestKitBase test, ActorSystem startOn, ActorSystem storeOn)
        {
            var probe = test.CreateTestProbe(storeOn);
            storeOn.ActorSelection("system/akka.persistence.journal.inmem").Tell(new Identify(null), probe.Ref);
            var sharedStore = probe.ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(20)).Subject;
            sharedStore.Should().NotBeNull();

            Persistence.Persistence.Instance.Apply(startOn);
            MemoryJournalShared.SetStore(sharedStore, startOn);
        }

        private static void SetSnapshotStore(this TestKitBase test, ActorSystem startOn, ActorSystem storeOn)
        {
            var probe = test.CreateTestProbe(storeOn);
            storeOn.ActorSelection("system/akka.persistence.snapshot-store.inmem").Tell(new Identify(null), probe.Ref);
            var sharedStore = probe.ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(20)).Subject;
            sharedStore.Should().NotBeNull();

            Persistence.Persistence.Instance.Apply(startOn);
            MemorySnapshotStoreShared.SetStore(sharedStore, startOn);
        }
    }
}
