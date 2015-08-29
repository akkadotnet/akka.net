using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DistributedData
{
    internal interface IGet
    {
        IKey Key { get; }
        IReadConsistency Consistency { get; }
        Object Request { get; }
    }

    public sealed class Get<T> : IGet, ICommand<T> where T : IReplicatedData
    {
        readonly Key<T> _key;
        readonly IReadConsistency _consistency;
        readonly Object _request;

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

        IKey<T> ICommand<T>.Key
        {
            get { return _key; }
        }

        public override bool Equals(object obj)
        {
            var other = obj as Get<T>;
            if(other != null)
            {
                var keysEqual = _key.Equals(other._key);
                var consistencyEqual = _consistency.Equals(other._consistency);
                bool requestsEqual = false;
                if (_request == null && other._request == null) { requestsEqual = true; }
                else if (_request != null) { requestsEqual = _request.Equals(other._request); }
                return keysEqual && consistencyEqual && requestsEqual;
            }
            return false;
        }

        IKey IGet.Key
        {
            get { return _key; }
        }
    }

    internal interface IGetResponse<T> where T : IReplicatedData
    {
        Key<T> Key { get; }
        Object Request { get; }
    }

    internal interface IGetSuccess
    {
        object Data { get; }
        IKey Key { get; }
        object Request { get; }
    }

    public sealed class GetSuccess<T> : IGetResponse<T>, IGetSuccess, IReplicatorMessage where T : IReplicatedData
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

        public GetSuccess(Key<T> key, Object request, T data)
        {
            _key = key;
            _request = request;
            _data = data;
        }

        object IGetSuccess.Data
        {
            get { return (object)_data; }
        }

        IKey IGetSuccess.Key
        {
            get { return (IKey)_key; }
        }

        public override bool Equals(object obj)
        {
            var other = obj as GetSuccess<T>;
            if (other != null)
            {
                var keysEqual = _key.Equals(other._key);
                var dataEqual = _data.Equals(other._data);
                bool requestsEqual = false;
                if (_request == null && other._request == null) { requestsEqual = true; }
                else if (_request != null) { requestsEqual = _request.Equals(other._request); }
                return keysEqual && dataEqual && requestsEqual;
            }
            return false;
        }
    }

    internal interface INotFound
    {
        IKey Key { get; }
        Object Request { get; }
    }

    public sealed class NotFound<T> : IGetResponse<T>, INotFound, IReplicatorMessage where T : IReplicatedData
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

        public NotFound(Key<T> key, object request)
        {
            _key = key;
            _request = request;
        }

        IKey INotFound.Key
        {
            get { return _key; }
        }

        public override bool Equals(object obj)
        {
            var other = obj as NotFound<T>;
            if(other != null)
            {
                bool requestsEqual = false;
                if (_request == null && other._request == null) { requestsEqual = true; }
                else if (_request != null) { requestsEqual = _request.Equals(other._request); }
                return _key.Equals(other._key) && requestsEqual;
            }
            return false;
        }

        public static NotFound<T> Create<T>(Key<T> key, object request) where T : IReplicatedData
        {
            return new NotFound<T>(key, request);
        }
    }

    internal interface IGetFailure
    {
        IKey Key { get; }
        Object Request { get; }
    }

    public sealed class GetFailure<T> : IGetResponse<T>, IGetFailure, IReplicatorMessage where T : IReplicatedData
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

        public GetFailure(Key<T> key, object request)
        {
            _key = key;
            _request = request;
        }

        IKey IGetFailure.Key
        {
            get { return _key; }
        }

        public override bool Equals(object obj)
        {
            var other = obj as GetFailure<T>;
            if (other != null)
            {
                bool requestsEqual = false;
                if (_request == null && other._request == null) { requestsEqual = true; }
                else if (_request != null) { requestsEqual = _request.Equals(other._request); }
                return _key.Equals(other._key) && requestsEqual;
            }
            return false;
        }
    }

}
