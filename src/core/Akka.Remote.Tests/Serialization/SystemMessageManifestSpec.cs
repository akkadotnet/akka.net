//-----------------------------------------------------------------------
// <copyright file="SystemMessageManifestSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch.SysMsg;
using Akka.Remote.Configuration;
using Akka.Remote.Serialization;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Serialization
{
    /// <summary>
    /// <see cref="SystemMessageSerializationSpec"/> round-trips through <c>FromBinary(bytes, Type)</c>, which
    /// never touches the string-manifest fallback a real remote deserialize uses. This round-trips every
    /// <see cref="SystemMessageSerializer"/> manifest through the real
    /// <see cref="Akka.Serialization.Serialization.Deserialize(byte[],int,string)"/> with
    /// <c>Akka.DynamicTypeLoading</c> off - the path an earlier draft of this fix got wrong because its test
    /// used a stand-in serializer instead.
    /// </summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class SystemMessageManifestSpec : AkkaSpec
    {
        private const string SwitchName = "Akka.DynamicTypeLoading";

        public SystemMessageManifestSpec(ITestOutputHelper output) : base(RemoteConfigFactory.Default(), output)
        {
        }

        [Fact(DisplayName = "Serialization should deserialize every SystemMessage manifest When dynamic type loading is off")]
        public async Task Should_deserialize_every_SystemMessage_manifest_When_dynamic_type_loading_is_disabled()
        {
            var child = ActorOf<BlackHoleActor>();
            var watchee = ActorOf<BlackHoleActor>().AsInstanceOf<IInternalActorRef>();
            var watcher = ActorOf<BlackHoleActor>().AsInstanceOf<IInternalActorRef>();

            object[] messages =
            {
                new Create(null),
                new Recreate(new Exception("boom")),
                new Suspend(),
                new Resume(new Exception("boom")),
                new Terminate(),
                new Supervise(child, true),
                new Watch(watchee, watcher),
                new Unwatch(watchee, watcher),
                new Failed(child, new Exception("boom"), 435345),
                new DeathWatchNotification(child, true, false)
            };

            foreach (var message in messages)
            {
                var deserialized = await RoundTripAsync(message);
                deserialized.Should().BeOfType(message.GetType(), because: message.GetType().Name);
            }
        }

        [Fact(DisplayName = "Serialization should decode a versioned assembly-qualified SystemMessage manifest When dynamic type loading is off")]
        public async Task Should_decode_a_versioned_assembly_qualified_manifest_When_dynamic_type_loading_is_disabled()
        {
            var serializer = Sys.Serialization.FindSerializerForType(typeof(Terminate));
            var bytes = serializer.ToBinary(new Terminate());
            const string manifest = "Akka.Dispatch.SysMsg.Terminate, Akka, Version=1.4.0.0, Culture=neutral, PublicKeyToken=null";

            await WithDynamicTypeLoadingOffAsync(() =>
            {
                Sys.Serialization.Deserialize(bytes, serializer.Identifier, manifest).Should().BeOfType<Terminate>();
                return Task.CompletedTask;
            });
        }

        /// <summary>Serializes with the real <see cref="SystemMessageSerializer"/>, then deserializes through the manifest string with the switch off.</summary>
        private async Task<object> RoundTripAsync(object message)
        {
            var serializer = Sys.Serialization.FindSerializerFor(message);
            serializer.Should().BeOfType<SystemMessageSerializer>();
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
    }
}
