using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DistributedData
{
    public sealed class Get<T> : ICommand<T>, IReplicatorMessage
    {
        readonly Key<T> _key;
        readonly IReadConsistency _consistency;
        readonly Object _request;

        public Key<T> Key
        {
            get { return _key; }
        }

        public IReadConsistency Consistency
        {
            get { return _consistency; }
        }

        public Object Request
        {
            get { return _request; }
        }

        public Get(Key<T> key, IReadConsistency consistency, Object request = null)
        {
            _key = key;
            _consistency = consistency;
            _request = request;
        }
    }

    internal interface IGetResponse<T> where T : IReplicatedData
    {
        Key<T> Key { get; }
        Object Request { get; }
    }

    public sealed class GetSuccess<T> : IGetResponse<T>, IReplicatorMessage
    {
        readonly Key<T> _key;
        readonly Object _request;
        readonly T _data;

        public Key<T> Key
        {
            get { return _key; }
        }

        public object Request
        {
            get { return _request; }
        }

        public T Data
        {
            get { return _data; }
        }

        internal GetSuccess(Key<T> key, Object request, T data)
        {
            _key = key;
            _request = request;
            _data = data;
        }
    }

    public sealed class NotFound<T> : IGetResponse<T>, IReplicatorMessage
    {
        readonly Key<T> _key;
        readonly object _request;

        public Key<T> Key
        {
            get { return _key; }
        }

        public object Request
        {
            get { return _request; }
        }

        internal NotFound(Key<T> key, object request)
        {
            _key = key;
            _request = request;
        }
    }

    public sealed class GetFailure<T> : IGetResponse<T>, IReplicatorMessage
    {
        private Key<T> _key;
        private object _request;

        public Key<T> Key
        {
            get { return _key; }
        }

        public object Request
        {
            get { return _request; }
        }

        internal GetFailure(Key<T> key, object request)
        {
            _key = key;
            _request = request;
        }
    }

}
