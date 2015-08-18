using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DistributedData
{
    internal interface ISubscribe
    {
        IKey Key { get; }
        IActorRef Subscriber { get; }
    }

    public sealed class Subscribe<T> : ISubscribe, IReplicatorMessage where T : IReplicatedData
    {
        readonly Key<T> _key;
        readonly IActorRef _subscriber;

        public Key<T> Key
        {
            get { return _key; }
        }

        public IActorRef Subscriber
        {
            get { return _subscriber; }
        }

        public Subscribe(Key<T> key, IActorRef subscriber)
        {
            _key = key;
            _subscriber = subscriber;
        }

        IKey ISubscribe.Key
        {
            get { return _key; }
        }

        public override bool Equals(object obj)
        {
            var other = obj as Subscribe<T>;
            if (other != null)
            {
                return _key.Equals(other._key) && _subscriber.Equals(other._subscriber);
            }
            return false;
        }
    }

    internal interface IUnsubscribe
    {
        IKey Key { get; }
        IActorRef Subscriber { get; }
    }

    public sealed class Unsubscribe<T> : IUnsubscribe, IReplicatorMessage where T : IReplicatedData
    {
        readonly Key<T> _key;
        readonly IActorRef _subscriber;

        public Key<T> Key
        {
            get { return _key; }
        }

        public IActorRef Subscriber
        {
            get { return _subscriber; }
        }

        public Unsubscribe(Key<T> key, IActorRef subscriber)
        {
            _key = key;
            _subscriber = subscriber;
        }

        IKey IUnsubscribe.Key
        {
            get { return _key; }
        }

        public override bool Equals(object obj)
        {
            var other = obj as Unsubscribe<T>;
            if(other != null)
            {
                return _key.Equals(other._key) && _subscriber.Equals(other._subscriber);
            }
            return false;
        }
    }

    public sealed class Changed<T> : IReplicatorMessage where T : IReplicatedData
    {
        readonly Key<T> _key;
        readonly T _data;

        public Key<T> Key
        {
            get { return _key; }
        }

        public T Data
        {
            get { return _data; }
        }

        public Changed(Key<T> key, T data)
        {
            _key = key;
            _data = data;
        }
    }
}
