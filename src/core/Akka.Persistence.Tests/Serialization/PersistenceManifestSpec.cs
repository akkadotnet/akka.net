//-----------------------------------------------------------------------
// <copyright file="PersistenceManifestSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Akka.Configuration;
using Akka.Persistence.Fsm;
using Akka.Persistence.Serialization;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Persistence.Tests.Serialization
{
    /// <summary>
    /// Round-trips every <see cref="PersistenceMessageSerializer"/>/<see cref="PersistenceSnapshotSerializer"/>
    /// manifest through the real <see cref="Akka.Serialization.Serialization.Deserialize(byte[],int,string)"/>
    /// with <c>Akka.DynamicTypeLoading</c> off, and confirms the one type deliberately left out of the table -
    /// the open generic <see cref="PersistentFSM.PersistentFSMSnapshot{TD}"/> - still throws a clear exception
    /// rather than silently resolving.
    /// </summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class PersistenceManifestSpec : AkkaSpec
    {
        private const string SwitchName = "Akka.DynamicTypeLoading";

        public PersistenceManifestSpec(ITestOutputHelper output)
            : base(ConfigurationFactory.FromResource<Persistence>("Akka.Persistence.persistence.conf"), output)
        {
        }

        [Fact(DisplayName = "Serialization should deserialize every Persistence envelope and a Snapshot manifest When dynamic type loading is off")]
        public async Task Should_deserialize_every_Persistence_manifest_When_dynamic_type_loading_is_disabled()
        {
            object[] messages =
            {
                new Persistent("hello", 1, "p1"),
                new AtomicWrite(new Persistent("hello", 1, "p1")),
                new AtLeastOnceDeliverySnapshot(17, new[] { new UnconfirmedDelivery(1, TestActor.Path, "a") }),
                new PersistentFSM.StateChangeEvent("a", TimeSpan.FromSeconds(10)),
                new Akka.Persistence.Serialization.Snapshot("hello")
            };

            foreach (var message in messages)
            {
                var deserialized = await RoundTripAsync(message);
                deserialized.Should().BeOfType(message.GetType(), because: message.GetType().Name);
            }
        }

        [Fact(DisplayName = "Serialization should decode a versioned assembly-qualified Snapshot manifest When dynamic type loading is off")]
        public async Task Should_decode_a_versioned_assembly_qualified_manifest_When_dynamic_type_loading_is_disabled()
        {
            var serializer = Sys.Serialization.FindSerializerForType(typeof(Akka.Persistence.Serialization.Snapshot));
            var bytes = serializer.ToBinary(new Akka.Persistence.Serialization.Snapshot("hello"));
            const string manifest = "Akka.Persistence.Serialization.Snapshot, Akka.Persistence, Version=1.4.0.0, Culture=neutral, PublicKeyToken=null";

            await WithDynamicTypeLoadingOffAsync(() =>
            {
                Sys.Serialization.Deserialize(bytes, serializer.Identifier, manifest).Should().BeOfType<Akka.Persistence.Serialization.Snapshot>();
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "Serialization should reject an unregistered PersistentFSMSnapshot manifest When dynamic type loading is off")]
        public async Task Should_reject_a_PersistentFSMSnapshot_manifest_When_dynamic_type_loading_is_disabled()
        {
            // the open generic PersistentFSM.PersistentFSMSnapshot<> isn't in PersistenceMessageSerializer's
            // table - a closed generic name can't be a compile-time key - so with the switch off it falls
            // through to the base reflection fallback and throws, instead of silently resolving.
            var message = new PersistentFSM.PersistentFSMSnapshot<FsmProbePayload>("a", new FsmProbePayload(), null);

            await Assert.ThrowsAsync<SerializationException>(() => RoundTripAsync(message));
        }

        /// <summary>Serializes with the real Persistence serializer, then deserializes through the manifest string with the switch off.</summary>
        private async Task<object> RoundTripAsync(object message)
        {
            var serializer = Sys.Serialization.FindSerializerFor(message);
            var bytes = serializer.ToBinary(message);
            var manifest = serializer.Manifest(message);
            var id = serializer.Identifier;

            object? result = null;
            await WithDynamicTypeLoadingOffAsync(() =>
            {
                result = Sys.Serialization.Deserialize(bytes, id, manifest);
                return Task.CompletedTask;
            });
            return result!;
        }

        private static async Task WithDynamicTypeLoadingOffAsync(Func<Task> body)
        {
            var hadSwitch = AppContext.TryGetSwitch(SwitchName, out var previous);
            AppContext.SetSwitch(SwitchName, false);
            try
            {
                await body();
            }
            finally
            {
                AppContext.SetSwitch(SwitchName, !hadSwitch || previous);
            }
        }

        /// <summary>Unique to this spec, so its manifest can't already be cached by another test's round trip.</summary>
        private sealed class FsmProbePayload
        {
        }
    }
}
