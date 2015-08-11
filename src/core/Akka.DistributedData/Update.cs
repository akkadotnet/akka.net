using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DistributedData
{
    public class Update<T> : ICommand<T>
    {
        private T ModifyWithInitial(T initial, Func<T,T> modifier, T data)
        {
            if(data == null)
            {
                return modifier(initial);
            }
            else
            {
                return modifier(data);
            }
        }

        readonly Key<T> _key;
        readonly IWriteConsistency _consistency;
        readonly Object _request;
        readonly Func<T, T> _modify;

        public Key<T> Key
        {
            get { return _key; }
        }

        public IWriteConsistency Consistency
        {
            get { return _consistency; }
        }

        public Object Request
        {
            get { return _request; }
        }

        public Func<T,T> Modify
        {
            get { return _modify; }
        }

        public Update(Key<T> key, IWriteConsistency consistency, Func<T,T> modify, Object requst = null)
        {
            _key = key;
            _consistency = consistency;
            _modify = modify;
            _request = requst;
        }
    }

    public interface IUpdateResponse<T>
    {
        Key<T> Key { get; }
        Object Request { get; }
    }

    public sealed class UpdateSuccess<T> : IUpdateResponse<T>
    {
        readonly Key<T> _key;
        readonly Object _request;

        public Key<T> Key
        {
            get { return _key; }
        }

        public object Request
        {
            get { return _request; }
        }

        public UpdateSuccess(Key<T> key, Object request)
        {
            _key = key;
            _request = request;
        }
    }

    public interface IUpdateFailure<T> : IUpdateResponse<T>
    { }

    public class UpdateTimeout<T> : IUpdateFailure<T>
    {
        private Key<T> _key;
        private Object _request;

        public Key<T> Key
        {
            get { return _key; }
        }

        public object Request
        {
            get { return _request; }
        }

        public UpdateTimeout(Key<T> key, Object request)
        {
            _key = key;
            _request = request;
        }
    }

    public class ModifyFailure<T> : IUpdateFailure<T>
    {
        private Key<T> _key;
        private Object _request;

        public Key<T> Key
        {
            get { return _key; }
        }

        public object Request
        {
            get { return _request; }
        }
    }
}
