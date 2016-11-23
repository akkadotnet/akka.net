//-----------------------------------------------------------------------
// <copyright file="Replicator.Messages.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
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
        public sealed class GetKeysIdsResult
        {
            public IImmutableSet<string> Keys { get; }

            public GetKeysIdsResult(IImmutableSet<string> keys)
            {
                Keys = keys;
            }

            public override bool Equals(object obj)
            {
                var other = obj as GetKeysIdsResult;
                return other != null && Keys.SetEquals(other.Keys);
            }
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

        internal interface IGetResponse : INoSerializationVerificationNeeded
        {
            IKey Key { get; }
            object Request { get; }
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

            public T Get<T>(IKey<T> key) where T : IReplicatedData => (T)Data; 

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

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Key.GetHashCode() * 397) ^ (Request != null ? Request.GetHashCode() : 0);
                }
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
                    return (Key.GetHashCode() * 397) ^ (Request != null ? Request.GetHashCode() : 0);
                }
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

        public interface IUpdateResponse
        {
            IKey Key { get; }
            object Request { get; }
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

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Key.GetHashCode() * 397) ^ (Request != null ? Request.GetHashCode() : 0);
                }
            }
        }

        public interface IUpdateFailure : IUpdateResponse { }

        /// <summary>
        /// The direct replication of the <see cref="Update{T}"/> could not be fulfill according to
        /// the given <see cref="IWriteConsistency"/> level and <see cref="IWriteConsistency.Timeout"/>.
        /// 
        /// The <see cref="Update{T}"/> was still performed locally and possibly replicated to some nodes.
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
                    return (Key.GetHashCode() * 397) ^ (Request != null ? Request.GetHashCode() : 0);
                }
            }
        }

        /// <summary>
        /// If the `modify` function of the <see cref="Update{T}"/> throws an exception the reply message
        /// will be this <see cref="ModifyFailure{T}"/> message. The original exception is included as <see cref="Cause"/>.
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

            public Delete(IKey key, IWriteConsistency consistency)
            {
                Key = key;
                Consistency = consistency;
            }
            public bool Equals(Delete other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(this, other)) return true;

                return Equals(Key, other.Key) && Equals(Consistency, other.Consistency);
            }

            public override bool Equals(object obj) => obj is Delete && Equals((Delete)obj);

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Key.GetHashCode() * 397) ^ (Consistency != null ? Consistency.GetHashCode() : 0);
                }
            }
        }

        public interface IDeleteResponse
        {
            IKey Key { get; }
        }

        [Serializable]
        public sealed class DeleteSuccess : IDeleteResponse, IEquatable<DeleteSuccess> 
        {
            public IKey Key { get; }

            public DeleteSuccess(IKey key)
            {
                Key = key;
            }

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

            public DataDeleted(IKey key)
            {
                Key = key;
            }

            public override string ToString() => $"DataDeleted {Key.Id}";

            public bool Equals(DataDeleted other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(this, other)) return true;

                return Equals(Key, other.Key);
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