//-----------------------------------------------------------------------
// <copyright file="Messages.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.DistributedData.Internal;

namespace Akka.DistributedData.Durable
{
    /// <summary>
    /// Request to store an entry. It optionally contains a <see cref="StoreReply"/>, which
    /// should be used to signal success or failure of the operation to the contained
    /// <see cref="StoreReply.ReplyTo"/> actor.
    /// </summary>
    public sealed class Store
    {
        public readonly string Key;
        public readonly DurableDataEnvelope Data;
        public readonly StoreReply Reply;

        public Store(string key, DurableDataEnvelope data, StoreReply reply = null)
        {
            Key = key;
            Data = data;
            Reply = reply;
        }
    }

    public sealed class StoreReply
    {
        public readonly object SuccessMessage;
        public readonly object FailureMessage;
        public readonly IActorRef ReplyTo;

        public StoreReply(object successMessage, object failureMessage, IActorRef replyTo)
        {
            SuccessMessage = successMessage;
            FailureMessage = failureMessage;
            ReplyTo = replyTo;
        }
    }

    /// <summary>
    /// Request to load all entries.
    /// 
    /// It must reply with 0 or more `LoadData` messages
    /// followed by one `LoadAllCompleted` message to the `sender` (the `Replicator`).
    /// 
    /// If the `LoadAll` fails it can throw `LoadFailedException` and the `Replicator` supervisor
    /// will stop itself and the durable store.
    /// </summary>
    public sealed class LoadAll : IEquatable<LoadAll>
    {
        public static readonly LoadAll Instance = new LoadAll();
        private LoadAll() { }
        public bool Equals(LoadAll other) => true;
        public override bool Equals(object obj) => obj is LoadAll;
    }

    public sealed class LoadData
    {
        public readonly ImmutableDictionary<string, DurableDataEnvelope> Data;

        public LoadData(ImmutableDictionary<string, DurableDataEnvelope> data)
        {
            Data = data;
        }
    }

    public sealed class LoadAllCompleted : IEquatable<LoadAllCompleted>
    {
        public static readonly LoadAllCompleted Instance = new LoadAllCompleted();
        private LoadAllCompleted() { }
        public bool Equals(LoadAllCompleted other) => true;
        public override bool Equals(object obj) => obj is LoadAllCompleted;
    }

    public sealed class LoadFailedException : AkkaException
    {
        public LoadFailedException(string message) : base(message)
        {
        }

        public LoadFailedException(string message, Exception cause) : base(message, cause)
        {
        }

#if SERIALIZATION
        public LoadFailedException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
#endif
    }

    public sealed class DurableDataEnvelope : IReplicatorMessage, IEquatable<DurableDataEnvelope>
    {
        internal DataEnvelope DataEnvelope { get; }
        public IReplicatedData Data => DataEnvelope.Data;

        public DurableDataEnvelope(DataEnvelope dataEnvelope)
        {
            DataEnvelope = dataEnvelope;
        }

        public DurableDataEnvelope(IReplicatedData data):this(new DataEnvelope(data))
        { }

        public override int GetHashCode()
        {
            return Data.GetHashCode();
        }

        public bool Equals(DurableDataEnvelope other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Data.Equals(other.Data);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is DurableDataEnvelope && Equals((DurableDataEnvelope) obj);
        }
    }
}
