//-----------------------------------------------------------------------
// <copyright file="SystemMessageSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Text;
using Akka.Actor;
using Akka.Dispatch.SysMsg;
using Akka.Serialization;
using Akka.Util;
using Akka.Util.Internal;
using Google.Protobuf;

namespace Akka.Remote.Serialization
{
    public sealed class SystemMessageSerializer : Serializer
    {
        private readonly WrappedPayloadSupport _payloadSupport;
        private ExceptionSupport _exceptionSupport;

        private static readonly byte[] EmptyBytes = {};

        /// <summary>
        /// Initializes a new instance of the <see cref="SystemMessageSerializer" /> class.
        /// </summary>
        /// <param name="system">The actor system to associate with this serializer. </param>
        public SystemMessageSerializer(ExtendedActorSystem system) : base(system)
        {
            _payloadSupport = new WrappedPayloadSupport(system);
            _exceptionSupport = new ExceptionSupport(system);
        }

        /// <inheritdoc />
        public override bool IncludeManifest { get; } = true; // TODO: should be false

        /// <inheritdoc />
        public override byte[] ToBinary(object obj)
        {
            if (obj is Create) return CreateToProto((Create)obj);
            if (obj is Recreate) return RecreateToProto((Recreate)obj);
            if (obj is Suspend) return EmptyBytes;
            if (obj is Resume) return ResumeToProto((Resume)obj);
            if (obj is Terminate) return EmptyBytes;
            if (obj is Supervise) return SuperviseToProto((Supervise)obj);
            if (obj is Watch) return WatchToProto((Watch)obj);
            if (obj is Unwatch) return UnwatchToProto((Unwatch)obj);
            if (obj is Failed) return FailedToProto((Failed)obj);
            if (obj is DeathWatchNotification) return DeathWatchNotificationToProto((DeathWatchNotification)obj);
            if (obj is NoMessage) throw new ArgumentException("NoMessage should never be serialized or deserialized");

            throw new ArgumentException($"Cannot serialize object of type [{obj.GetType().TypeQualifiedName()}]");
        }

        /// <inheritdoc />
        public override object FromBinary(byte[] bytes, Type type)
        {
            if (type == typeof(Create)) return CreateFromProto(bytes);
            if (type == typeof(Recreate)) return RecreateFromProto(bytes);
            if (type == typeof(Suspend)) return new Suspend();
            if (type == typeof(Resume)) return ResumeFromProto(bytes);
            if (type == typeof(Terminate)) return new Terminate();
            if (type == typeof(Supervise)) return SuperviseFromProto(bytes);
            if (type == typeof(Watch)) return WatchFromProto(bytes);
            if (type == typeof(Unwatch)) return UnwatchFromProto(bytes);
            if (type == typeof(Failed)) return FailedFromProto(bytes);
            if (type == typeof(DeathWatchNotification)) return DeathWatchNotificationFromProto(bytes);

            throw new ArgumentException($"Unimplemented deserialization of message with manifest [{type.TypeQualifiedName()}] in [${nameof(SystemMessageSerializer)}]");
        }

        //
        // Create
        //
        private byte[] CreateToProto(Create create)
        {
            var message = new Proto.Msg.CreateData();
            message.Cause = _exceptionSupport.ExceptionToProto(create.Failure);
            return message.ToByteArray();
        }

        private Create CreateFromProto(byte[] bytes)
        {
            var proto = Proto.Msg.CreateData.Parser.ParseFrom(bytes);
            var payload = (ActorInitializationException)_exceptionSupport.ExceptionFromProto(proto.Cause);
            return new Create(payload);
        }

        //
        // Recreate
        //
        private byte[] RecreateToProto(Recreate recreate)
        {
            var message = new Proto.Msg.RecreateData();
            message.Cause = _exceptionSupport.ExceptionToProto(recreate.Cause);
            return message.ToByteArray();
        }

        private Recreate RecreateFromProto(byte[] bytes)
        {
            var proto = Proto.Msg.RecreateData.Parser.ParseFrom(bytes);
            var payload = (Exception)_exceptionSupport.ExceptionFromProto(proto.Cause);
            return new Recreate(payload);
        }

        //
        // Recreate
        //
        private byte[] ResumeToProto(Resume resume)
        {
            var message = new Proto.Msg.ResumeData();
            message.Cause = _exceptionSupport.ExceptionToProto(resume.CausedByFailure);
            return message.ToByteArray();
        }

        private Resume ResumeFromProto(byte[] bytes)
        {
            var proto = Proto.Msg.ResumeData.Parser.ParseFrom(bytes);
            var payload = (Exception)_exceptionSupport.ExceptionFromProto(proto.Cause);
            return new Resume(payload);
        }

        //
        // Supervise
        //
        private byte[] SuperviseToProto(Supervise supervise)
        {
            var message = new Proto.Msg.SuperviseData();
            message.Child = new Proto.Msg.ActorRefData();
            message.Child.Path = Akka.Serialization.Serialization.SerializedActorPath(supervise.Child);
            message.Async = supervise.Async;
            return message.ToByteArray();
        }

        private Supervise SuperviseFromProto(byte[] bytes)
        {
            var proto = Proto.Msg.SuperviseData.Parser.ParseFrom(bytes);
            return new Supervise(ResolveActorRef(proto.Child.Path), proto.Async);
        }

        //
        // Watch
        //
        private byte[] WatchToProto(Watch watch)
        {
            var message = new Proto.Msg.WatchData();
            message.Watchee = new Proto.Msg.ActorRefData();
            message.Watchee.Path = Akka.Serialization.Serialization.SerializedActorPath(watch.Watchee);
            message.Watcher = new Proto.Msg.ActorRefData();
            message.Watcher.Path = Akka.Serialization.Serialization.SerializedActorPath(watch.Watcher);
            return message.ToByteArray();
        }

        private Watch WatchFromProto(byte[] bytes)
        {
            var proto = Proto.Msg.WatchData.Parser.ParseFrom(bytes);
            return new Watch(
                ResolveActorRef(proto.Watchee.Path).AsInstanceOf<IInternalActorRef>(),
                ResolveActorRef(proto.Watcher.Path).AsInstanceOf<IInternalActorRef>());
        }

        //
        // Unwatch
        //
        private byte[] UnwatchToProto(Unwatch unwatch)
        {
            var message = new Proto.Msg.WatchData();
            message.Watchee = new Proto.Msg.ActorRefData();
            message.Watchee.Path = Akka.Serialization.Serialization.SerializedActorPath(unwatch.Watchee);
            message.Watcher = new Proto.Msg.ActorRefData();
            message.Watcher.Path = Akka.Serialization.Serialization.SerializedActorPath(unwatch.Watcher);
            return message.ToByteArray();
        }

        private Unwatch UnwatchFromProto(byte[] bytes)
        {
            var proto = Proto.Msg.WatchData.Parser.ParseFrom(bytes);
            return new Unwatch(
                ResolveActorRef(proto.Watchee.Path).AsInstanceOf<IInternalActorRef>(),
                ResolveActorRef(proto.Watcher.Path).AsInstanceOf<IInternalActorRef>());
        }

        //
        // Failed
        //
        private byte[] FailedToProto(Failed failed)
        {
            var message = new Proto.Msg.FailedData();
            message.Cause = _exceptionSupport.ExceptionToProto(failed.Cause);
            message.Child = new Proto.Msg.ActorRefData();
            message.Child.Path = Akka.Serialization.Serialization.SerializedActorPath(failed.Child);
            message.Uid = (ulong)failed.Uid;
            return message.ToByteArray();
        }

        private Failed FailedFromProto(byte[] bytes)
        {
            var proto = Proto.Msg.FailedData.Parser.ParseFrom(bytes);

            return new Failed(
                ResolveActorRef(proto.Child.Path),
                (Exception)_exceptionSupport.ExceptionFromProto(proto.Cause),
                (long)proto.Uid);
        }

        //
        // DeathWatchNotification
        //
        private byte[] DeathWatchNotificationToProto(DeathWatchNotification deathWatchNotification)
        {
            var message = new Proto.Msg.DeathWatchNotificationData();
            message.Actor = new Proto.Msg.ActorRefData();
            message.Actor.Path = Akka.Serialization.Serialization.SerializedActorPath(deathWatchNotification.Actor);
            message.AddressTerminated = deathWatchNotification.AddressTerminated;
            message.ExistenceConfirmed = deathWatchNotification.ExistenceConfirmed;
            return message.ToByteArray();
        }

        private DeathWatchNotification DeathWatchNotificationFromProto(byte[] bytes)
        {
            var proto = Proto.Msg.DeathWatchNotificationData.Parser.ParseFrom(bytes);

            return new DeathWatchNotification(
                system.Provider.ResolveActorRef(proto.Actor.Path),
                proto.ExistenceConfirmed,
                proto.AddressTerminated);
        }

        //
        // ActorRef
        //
        private IActorRef ResolveActorRef(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            return system.Provider.ResolveActorRef(path);
        }
    }
}
