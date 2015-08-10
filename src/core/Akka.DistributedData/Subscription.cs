using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DistributedData
{
    public sealed class Subscribe<T> where T : IReplicatedData
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
    }

    public sealed class Unsubscribe<T> where T : IReplicatedData
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
    }

    public sealed class Changed<T> where T : IReplicatedData
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
