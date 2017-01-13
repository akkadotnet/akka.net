#region copyright
// -----------------------------------------------------------------------
//  <copyright file="Messages.cs" company="Akka.NET project">
//      Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2017 Akka.NET project <https://github.com/akkadotnet>
//  </copyright>
// -----------------------------------------------------------------------
#endregion

using System;
using System.Collections.Immutable;
using Akka.Actor;

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
        public readonly IReplicatedData Data;
        public readonly StoreReply Reply;

        public Store(string key, IReplicatedData data, StoreReply reply = null)
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
    /// If the `LoadAll` fails it can throw `LoadFailed` and the `Replicator` supervisor
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
        public readonly ImmutableDictionary<string, IReplicatedData> Data;

        public LoadData(ImmutableDictionary<string, IReplicatedData> data)
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

    public sealed class LoadFailed
    {
        public readonly string Message;
        public readonly Exception Cause;

        public LoadFailed(string message, Exception cause)
        {
            Message = message;
            Cause = cause;
        }
    }

    public sealed class DurableDataEnvelope : IReplicatorMessage, IEquatable<DurableDataEnvelope>
    {
        public readonly IReplicatedData Data;

        public DurableDataEnvelope(IReplicatedData data)
        {
            Data = data;
        }

        public override int GetHashCode()
        {
            return Data.GetHashCode();
        }

        public bool Equals(DurableDataEnvelope other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(Data, other.Data);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is DurableDataEnvelope && Equals((DurableDataEnvelope) obj);
        }
    }
}