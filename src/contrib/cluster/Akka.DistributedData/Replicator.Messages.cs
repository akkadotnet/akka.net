//-----------------------------------------------------------------------
// <copyright file="Replicator.Messages.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using Akka.Actor;

namespace Akka.DistributedData
{
    public partial class Replicator
    {
        #region messages

        [Serializable]
        public class GetKeyIds
        {
            public static readonly GetKeyIds Instance = new GetKeyIds();

            private GetKeyIds() { }

            public override bool Equals(object obj) => obj is GetKeyIds;
        }

        [Serializable]
        public sealed class GetKeysIdsResult : IEquatable<GetKeysIdsResult>
        {
            public IImmutableSet<string> Keys { get; }

            public GetKeysIdsResult(IImmutableSet<string> keys)
            {
                Keys = keys;
            }

            public override bool Equals(object obj) => 
                obj is GetKeysIdsResult && Equals((GetKeysIdsResult) obj);

            public bool Equals(GetKeysIdsResult other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(this, other)) return true;

                return Equals(Keys, other.Keys);
            }

            public override int GetHashCode() => Keys.GetHashCode();
        }

        internal interface ICommand
        {
            IKey Key { get; }
        }

        /// <summary>
        /// Send this message to the local <see cref="Replicator"/> to retrieve a data value for the
        /// given `key`. The `Replicator` will reply with one of the <see cref="GetResponse"/> messages.
        /// 
        /// The optional `request` context is included in the reply messages. This is a convenient
        /// way to pass contextual information (e.g. original sender) without having to use `ask`
        /// or maintain local correlation data structures.
        /// </summary>
        [Serializable]
        public sealed class Get : ICommand, IEquatable<Get>, IReplicatorMessage
        {
            public IKey Key { get; }
            public IReadConsistency Consistency { get; }
            public object Request { get; }

            public Get(IKey key, IReadConsistency consistency, object request = null)
            {
                Key = key;
                Consistency = consistency;
                Request = request;
            }

            public bool Equals(Get other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(this, other)) return true;

                return Equals(Key, other.Key) && Equals(Request, other.Request) &&
                       Equals(Consistency, other.Consistency);
            }

            public override bool Equals(object obj) => obj is Get && Equals((Get)obj);

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = Key.GetHashCode();
                    hashCode = (hashCode * 397) ^ (Consistency != null ? Consistency.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (Request != null ? Request.GetHashCode() : 0);
                    return hashCode;
                }
            }
        }

        /// <summary>
        /// Common response interface on <see cref="Get"/> request. It can take one of 
        /// the tree possible values:
        /// 1. <see cref="GetSuccess"/> with the result of the request.
        /// 2. <see cref="NotFound"/> when a value for requested key didn't exist.
        /// 3. <see cref="GetFailure"/> when an exception happened when fulfilling the request.
        /// </summary>
        public interface IGetResponse : INoSerializationVerificationNeeded
        {
            /// <summary>
            /// Initial key send by <see cref="Get"/> request.
            /// </summary>
            IKey Key { get; }

            /// <summary>
            /// Optional object used for request/response correlation.
            /// </summary>
            object Request { get; }

            /// <summary>
            /// True if value for request was successfully returned.
            /// False if value was either not found or ended with failure.
            /// </summary>
            bool IsSuccessful { get; }

            /// <summary>
            /// False if value for request was not found. True otherwise.
            /// </summary>
            bool IsFound { get; }

            /// <summary>
            /// True if a failure happened during request fulfilment.
            /// False if returned successfully or value not found for the key.
            /// </summary>
            bool IsFailure { get; }

            /// <summary>
            /// Tries to return a result of the request, given a replicated collection 
            /// <paramref name="key"/> used when sending a <see cref="Replicator.Get"/> request.
            /// </summary>
            /// <typeparam name="T">Replicated data.</typeparam>
            /// <param name="key">Key send originally with a <see cref="Replicator.Get"/> request.</param>
            /// <exception cref="KeyNotFoundException">Thrown when no value for provided <paramref name="key"/> was found.</exception>
            /// <exception cref="TimeoutException">Thrown when response with given consistency didn't arrive within specified timeout.</exception>
            /// <returns></returns>
            T Get<T>(IKey<T> key) where T : IReplicatedData;
        }

        [Serializable]
        public sealed class GetSuccess : IGetResponse, IEquatable<GetSuccess>, IReplicatorMessage
        {
            public IKey Key { get; }
            public object Request { get; }
            public IReplicatedData Data { get; }

            /// <summary>
            /// Reply from <see cref="Get"/>. The data value is retrieved with <see cref="Data"/>.
            /// </summary>
            public GetSuccess(IKey key, object request, IReplicatedData data)
            {
                Key = key;
                Request = request;
                Data = data;
            }

            public bool Equals(GetSuccess other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(this, other)) return true;

                return Equals(Key, other.Key) && Equals(Request, other.Request) && Equals(Data, other.Data);
            }

            public override bool Equals(object obj) => obj is GetSuccess && Equals((GetSuccess)obj);

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = Key.GetHashCode();
                    hashCode = (hashCode * 397) ^ (Request?.GetHashCode() ?? 0);
                    hashCode = (hashCode * 397) ^ Data?.GetHashCode() ?? 0;
                    return hashCode;
                }
            }

            public bool IsSuccessful => true;
            public bool IsFound => true;
            public bool IsFailure => false;

            public T Get<T>(IKey<T> key) where T : IReplicatedData
            {
                if (Data is T) return (T) Data;

                throw new InvalidCastException($"Response returned for key '{Key}' is of type [{Data?.GetType()}] and cannot be casted using key '{key}' to type [{typeof(T)}]");
            }
        }

        [Serializable]
        public sealed class NotFound : IGetResponse, IEquatable<NotFound>, IReplicatorMessage
        {
            public IKey Key { get; }

            public object Request { get; }

            public NotFound(IKey key, object request)
            {
                Key = key;
                Request = request;
            }

            public bool Equals(NotFound other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(this, other)) return true;

                return Equals(Key, other.Key) && Equals(Request, other.Request);
            }

            public override bool Equals(object obj) => obj is NotFound && Equals((NotFound)obj);

            public override string ToString() => $"NotFound({Key}, {Request})";

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Key.GetHashCode() * 397) ^ (Request?.GetHashCode() ?? 0);
                }
            }

            public bool IsSuccessful => false;
            public bool IsFound => false;
            public bool IsFailure => false;

            public T Get<T>(IKey<T> key) where T : IReplicatedData
            {
                throw new KeyNotFoundException($"No value was found for the key '{Key}'");
            }
        }

        /// <summary>
        /// The <see cref="Get{T}"/> request could not be fulfill according to the given
        /// <see cref="IReadConsistency"/> level and <see cref="IReadConsistency.Timeout"/> timeout.
        /// </summary>
        [Serializable]
        public sealed class GetFailure : IGetResponse, IEquatable<GetFailure>, IReplicatorMessage
        {
            public IKey Key { get; }
            public object Request { get; }

            public GetFailure(IKey key, object request)
            {
                Key = key;
                Request = request;
            }

            public bool Equals(GetFailure other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(this, other)) return true;

                return Equals(Key, other.Key) && Equals(Request, other.Request);
            }

            public override bool Equals(object obj) => obj is GetFailure && Equals((GetFailure)obj);

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Key.GetHashCode() * 397) ^ (Request?.GetHashCode() ?? 0);
                }
            }

            public bool IsSuccessful => false;
            public bool IsFound => true;
            public bool IsFailure => true;

            public T Get<T>(IKey<T> key) where T : IReplicatedData
            {
                throw new TimeoutException($"A timeout occurred when trying to retrieve a value for key '{Key}' withing given read consistency");
            }
        }
        
        /// <summary>
        /// Register a subscriber that will be notified with a <see cref="Changed{T}"/> message
        /// when the value of the given <see cref="Key"/> is changed. Current value is also
        /// sent as a <see cref="Changed{T}"/> message to a new subscriber.
        /// 
        /// Subscribers will be notified periodically with the configured `notify-subscribers-interval`,
        /// and it is also possible to send an explicit `FlushChanges` message to
        /// the <see cref="Replicator"/> to notify the subscribers immediately.
        /// 
        /// The subscriber will automatically be unregistered if it is terminated.
        /// 
        /// If the key is deleted the subscriber is notified with a <see cref="DataDeleted{T}"/> message.
        /// </summary>
        [Serializable]
        public sealed class Subscribe : IReplicatorMessage, IEquatable<Subscribe> 
        {
            public IKey Key { get; }

            public IActorRef Subscriber { get; }

            public Subscribe(IKey key, IActorRef subscriber)
            {
                Key = key;
                Subscriber = subscriber;
            }
            
            public bool Equals(Subscribe other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(this, other)) return true;

                return Equals(Key, other.Key) && Equals(Subscriber, other.Subscriber);
            }

            public override bool Equals(object obj) => obj is Subscribe && Equals((Subscribe)obj);

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Key.GetHashCode() * 397) ^ (Subscriber != null ? Subscriber.GetHashCode() : 0);
                }
            }
        }
        
        /// <summary>
        /// Unregister a subscriber.
        /// </summary>
        /// <seealso cref="Subscribe{T}"/>
        [Serializable]
        public sealed class Unsubscribe : IEquatable<Unsubscribe>, IReplicatorMessage
        {
            public IKey Key { get; }
            public IActorRef Subscriber { get; }

            public Unsubscribe(IKey key, IActorRef subscriber)
            {
                Key = key;
                Subscriber = subscriber;
            }
            
            public bool Equals(Unsubscribe other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(this, other)) return true;

                return Equals(Key, other.Key) && Equals(Subscriber, other.Subscriber);
            }

            public override bool Equals(object obj) => obj is Unsubscribe && Equals((Unsubscribe)obj);

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Key.GetHashCode() * 397) ^ (Subscriber != null ? Subscriber.GetHashCode() : 0);
                }
            }
        }

        internal interface IChanged
        {
            IKey Key { get; }
            object Data { get; }
        }

        /// <summary>
        /// The data value is retrieved with <see cref="Data"/> using the typed key.
        /// </summary>
        /// <seealso cref="Subscribe{T}"/>
        [Serializable]
        public sealed class Changed : IChanged, IEquatable<Changed>, IReplicatorMessage 
        {
            public IKey Key { get; }
            public object Data { get; }

            public Changed(IKey key, object data)
            {
                Key = key;
                Data = data;
            }

            IKey IChanged.Key => Key;

            public bool Equals(Changed other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(this, other)) return true;

                return Equals(Key, other.Key) && Equals(Data, other.Data);
            }

            public T Get<T>(IKey<T> key)
            {
                if (!Equals(Key, key)) throw new ArgumentException("Wrong key used, must be contained key");
                return (T) Data;
            }

            public override bool Equals(object obj) => obj is Changed && Equals((Changed)obj);

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Key.GetHashCode() * 397) ^ Data?.GetHashCode() ?? 0;
                }
            }
        }

        /// <summary>
        /// Send this message to the local <see cref="Replicator"/> to update a data value for the
        /// given <see cref="Key"/>. The <see cref="Replicator"/> will reply with one of the 
        /// <see cref="IUpdateResponse{T}"/> messages.
        /// 
        /// The current data value for the <see cref="Key"/> is passed as parameter to the <see cref="Modify"/> function.
        /// It is `null` if there is no value for the <see cref="Key"/>, and otherwise <see cref="Request"/>. The function
        /// is supposed to return the new value of the data, which will then be replicated according to
        /// the given <see cref="IWriteConsistency"/>.
        /// 
        /// The <see cref="Modify"/> function is called by the `<see cref="Replicator"/>` actor and must therefore be a pure
        /// function that only uses the data parameter and stable fields from enclosing scope. It must
        /// for example not access `sender()` reference of an enclosing actor.
        /// </summary>
        [Serializable]
        public sealed class Update : ICommand 
        {
            private IReplicatedData ModifyWithInitial(IReplicatedData initial, Func<IReplicatedData, IReplicatedData> modifier, IReplicatedData data) =>
                modifier(data ?? initial);

            public IKey Key { get; }
            public IWriteConsistency Consistency { get; }
            public object Request { get; }
            public Func<IReplicatedData, IReplicatedData> Modify { get; }

            public Update(IKey key, IWriteConsistency consistency, Func<IReplicatedData, IReplicatedData> modify, object request = null)
            {
                Key = key;
                Consistency = consistency;
                Modify = modify;
                Request = request;
            }

            /// <summary>
            /// Modify value of local <see cref="Replicator"/> and replicate with given <see cref="IWriteConsistency"/>.
            /// 
            /// The current value for the <see cref="Key"/> is passed to the <see cref="Modify"/> function.
            /// If there is no current data value for the <see cref="Key"/> the <paramref name="initial"/> value will be
            /// passed to the <see cref="Modify"/> function.
            /// 
            /// The optional <paramref name="request"/> context is included in the reply messages. This is a convenient
            /// way to pass contextual information (e.g. original sender) without having to use `ask`
            /// or local correlation data structures.
            /// </summary>
            public Update(IKey key, IReplicatedData initial, IWriteConsistency consistency, Func<IReplicatedData, IReplicatedData> modify, object request = null)
            {
                Key = key;
                Consistency = consistency;
                Request = request;
                Modify = x => ModifyWithInitial(initial, modify, x);
            }
        }

        /// <summary>
        /// A response message for the <see cref="Update"/> request. It can be one of the 3 possible types:
        /// 1. <see cref="UpdateSuccess"/> when update has finished successfully with given write consistency withing provided time limit.
        /// 2. <see cref="ModifyFailure"/> if a <see cref="Update.Modify"/> delegate has thrown a failure.
        /// 3. <see cref="UpdateTimeout"/> if a request couldn't complete withing given timeout and write consistency constraints.
        /// </summary>
        public interface IUpdateResponse
        {
            /// <summary>
            /// Key, under with updated data is going to be stored.
            /// </summary>
            IKey Key { get; }

            /// <summary>
            /// Optional object that can be used to correlate this response with particular <see cref="Update"/> request.
            /// </summary>
            object Request { get; }

            /// <summary>
            /// Returns true if <see cref="Update"/> request has completed successfully.
            /// </summary>
            bool IsSuccessful { get; }

            /// <summary>
            /// Throws an exception if <see cref="Update"/> request has failed.
            /// </summary>
            void ThrowOnFailure();
        }

        [Serializable]
        public sealed class UpdateSuccess : IUpdateResponse, IEquatable<UpdateSuccess>, INoSerializationVerificationNeeded
        {
            public IKey Key { get; }
            public object Request { get; }

            public UpdateSuccess(IKey key, object request)
            {
                Key = key;
                Request = request;
            }

            public bool Equals(UpdateSuccess other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(this, other)) return true;

                return Equals(Key, other.Key) && Equals(Request, other.Request);
            }

            public override bool Equals(object obj) => obj is UpdateSuccess && Equals((UpdateSuccess)obj);

            public override string ToString() => $"UpdateSuccess({Key}, {Request})";

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Key.GetHashCode() * 397) ^ (Request?.GetHashCode() ?? 0);
                }
            }

            public bool IsSuccessful => true;
            public void ThrowOnFailure() { }
        }

        /// <summary>
        /// A common interface for <see cref="Update"/> responses that have ended with a failure.
        /// </summary>
        public interface IUpdateFailure : IUpdateResponse
        {
            /// <summary>
            /// Returns a cause of the exception.
            /// </summary>
            Exception Cause { get; }
        }

        /// <summary>
        /// The direct replication of the <see cref="Update"/> could not be fulfill according to
        /// the given <see cref="IWriteConsistency"/> level and <see cref="IWriteConsistency.Timeout"/>.
        /// 
        /// The <see cref="Update"/> was still performed locally and possibly replicated to some nodes.
        /// It will eventually be disseminated to other replicas, unless the local replica
        /// crashes before it has been able to communicate with other replicas.
        /// </summary>
        [Serializable]
        public sealed class UpdateTimeout : IUpdateFailure, IEquatable<UpdateTimeout>
        {
            public IKey Key { get; }
            public object Request { get; }

            public UpdateTimeout(IKey key, object request)
            {
                Key = key;
                Request = request;
            }

            public bool Equals(UpdateTimeout other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(this, other)) return true;

                return Equals(Key, other.Key) && Equals(Request, other.Request);
            }

            public override bool Equals(object obj) => obj is UpdateTimeout && Equals((UpdateTimeout)obj);

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Key.GetHashCode() * 397) ^ (Request?.GetHashCode() ?? 0);
                }
            }

            public bool IsSuccessful => false;
            public Exception Cause => new TimeoutException($"An update for key '{Key}' didn't completed within given timeout and write consistency constraints.");

            public void ThrowOnFailure()
            {
                ExceptionDispatchInfo.Capture(Cause).Throw();
            }
        }

        /// <summary>
        /// If the `modify` function of the <see cref="Update"/> throws an exception the reply message
        /// will be this <see cref="ModifyFailure"/> message. The original exception is included as <see cref="Cause"/>.
        /// </summary>
        [Serializable]
        public sealed class ModifyFailure : IUpdateFailure 
        {
            public IKey Key { get; }
            public object Request { get; }
            public string ErrorMessage { get; }
            public Exception Cause { get; }

            public ModifyFailure(IKey key, string errorMessage, Exception cause, object request)
            {
                Key = key;
                Request = request;
                ErrorMessage = errorMessage;
                Cause = cause;
            }

            public override string ToString() => $"ModifyFailure {Key}: {ErrorMessage}";
            public bool IsSuccessful => false;

            public void ThrowOnFailure()
            {
                ExceptionDispatchInfo.Capture(Cause).Throw();
            }
        }

        /// <summary>
        /// The local store or direct replication of the <see cref="Update"/> could not be fulfill according to
        /// the given <see cref="IWriteConsistency"/> due to durable store errors. This is
        /// only used for entries that have been configured to be durable.
        /// 
        /// The <see cref="Update"/> was still performed in memory locally and possibly replicated to some nodes,
        /// but it might not have been written to durable storage.
        /// It will eventually be disseminated to other replicas, unless the local replica
        /// crashes before it has been able to communicate with other replicas.
        /// </summary>
        public sealed class StoreFailure : IUpdateFailure, IDeleteResponse, IEquatable<StoreFailure>
        {
            private readonly IKey _key;
            private readonly object _request;

            public StoreFailure(IKey key, object request = null)
            {
                _key = key;
                _request = request;
            }

            IKey IUpdateResponse.Key => _key;
            bool IDeleteResponse.IsSuccessful => false;

            public bool AlreadyDeleted => false;
            public object Request => _request;

            IKey IDeleteResponse.Key => _key;
            bool IUpdateResponse.IsSuccessful => false;

            public void ThrowOnFailure()
            {
                throw Cause;
            }

            public Exception Cause => new Exception($"Failed to store value under the key {_key}");

            public bool Equals(StoreFailure other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return Equals(_key, other._key) && Equals(_request, other._request);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                return obj is StoreFailure && Equals((StoreFailure) obj);
            }

            public override string ToString() => $"StoreFailure({_key}, {_request})";

            public override int GetHashCode()
            {
                unchecked
                {
                    return (_key.GetHashCode()* 397) ^ (_request?.GetHashCode() ?? 0);
                }
            }
        }
        
        /// <summary>
        /// Send this message to the local <see cref="Replicator"/> to delete a data value for the
        /// given <see cref="Key"/>. The <see cref="Replicator"/> will reply with one of the <see cref="IDeleteResponse{T}"/> messages.
        /// </summary>
        [Serializable]
        public sealed class Delete : ICommand, IEquatable<Delete>
        {
            public IKey Key { get; }
            public IWriteConsistency Consistency { get; }
            public object Request { get; }

            public Delete(IKey key, IWriteConsistency consistency, object request = null)
            {
                Key = key;
                Consistency = consistency;
                Request = request;
            }
            public bool Equals(Delete other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(this, other)) return true;

                return Equals(Key, other.Key) && Equals(Consistency, other.Consistency) && Equals(Request, other.Request);
            }

            public override bool Equals(object obj) => obj is Delete && Equals((Delete)obj);

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Key.GetHashCode() * 397) ^ (Consistency?.GetHashCode() ?? 0);
                }
            }
        }

        /// <summary>
        /// A response for a possible <see cref="Delete"/> request message. It can be one of 3 possible cases:
        /// 1. <see cref="DeleteSuccess"/> when data was deleted successfully.
        /// 2. <see cref="ReplicationDeletedFailure"/> when delete operation ended with failure.
        /// 3. <see cref="DataDeleted"/> when an operation attempted to delete already deleted data.
        /// </summary>
        public interface IDeleteResponse
        {
            /// <summary>
            /// Key, for which data was deleted.
            /// </summary>
            IKey Key { get; }

            /// <summary>
            /// Returns true if value for provided <see cref="Key"/> was either successfully deleted, or was deleted already.
            /// </summary>
            bool IsSuccessful { get; }

            /// <summary>
            /// Returns true if value for provided <see cref="Key"/> was already deleted.
            /// </summary>
            bool AlreadyDeleted { get; }
        }

        [Serializable]
        public sealed class DeleteSuccess : IDeleteResponse, IEquatable<DeleteSuccess> 
        {
            public IKey Key { get; }
            public object Request { get; }

            public DeleteSuccess(IKey key, object request = null)
            {
                Key = key;
                Request = request;
            }
            public bool IsSuccessful => true;
            public bool AlreadyDeleted => false;

            public bool Equals(DeleteSuccess other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(this, other)) return true;

                return Equals(Key, other.Key);
            }

            public override bool Equals(object obj) => obj is DeleteSuccess && Equals((DeleteSuccess)obj);

            public override int GetHashCode() => Key.GetHashCode();
        }

        [Serializable]
        public sealed class ReplicationDeletedFailure : IDeleteResponse, IEquatable<ReplicationDeletedFailure>
        {
            public IKey Key { get; }

            public ReplicationDeletedFailure(IKey key)
            {
                Key = key;
            }
            public bool IsSuccessful => false;
            public bool AlreadyDeleted => false;

            public bool Equals(ReplicationDeletedFailure other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(this, other)) return true;

                return Equals(Key, other.Key);
            }

            public override bool Equals(object obj) => obj is ReplicationDeletedFailure && Equals((ReplicationDeletedFailure)obj);

            public override int GetHashCode() => Key.GetHashCode();
        }

        [Serializable]
        public sealed class DataDeleted : Exception, IDeleteResponse, IEquatable<DataDeleted>
        {
            public IKey Key { get; }
            public object Request { get; }

            public DataDeleted(IKey key, object request = null)
            {
                Key = key;
                Request = request;
            }
            public bool IsSuccessful => true;
            public bool AlreadyDeleted => true;

            public override string ToString() => $"DataDeleted {Key.Id}";

            public bool Equals(DataDeleted other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(this, other)) return true;

                return Equals(Key, other.Key) && Equals(Request, other.Request);
            }

            public override bool Equals(object obj) => obj is DataDeleted && Equals((DataDeleted)obj);

            public override int GetHashCode() => Key.GetHashCode();
        }

        /// <summary>
        /// Get current number of replicas, including the local replica.
        /// Will reply to sender with <see cref="ReplicaCount"/>.
        /// </summary>
        [Serializable]
        public sealed class GetReplicaCount
        {
            public static readonly GetReplicaCount Instance = new GetReplicaCount();

            private GetReplicaCount() { }
        }

        /// <summary>
        /// Current number of replicas. Reply to <see cref="GetReplicaCount"/>.
        /// </summary>
        [Serializable]
        public sealed class ReplicaCount : IEquatable<ReplicaCount>
        {
            public int N { get; }

            public ReplicaCount(int n)
            {
                N = n;
            }

            public bool Equals(ReplicaCount other) => other != null && N == other.N;

            public override bool Equals(object obj) => obj is ReplicaCount && Equals((ReplicaCount) obj);

            public override int GetHashCode() => N.GetHashCode();

            public override string ToString() => $"ReplicaCount({N})";
        }

        /// <summary>
        /// Notify subscribers of changes now, otherwise they will be notified periodically
        /// with the configured `notify-subscribers-interval`.
        /// </summary>
        [Serializable]
        public sealed class FlushChanges
        {
            public static readonly FlushChanges Instance = new FlushChanges();

            private FlushChanges() { }

            public override bool Equals(object obj)
            {
                return obj is FlushChanges;
            }
        }

        #endregion
    }

    public interface IReplicatorMessage { }
}