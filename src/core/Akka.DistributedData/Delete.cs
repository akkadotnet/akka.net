using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DistributedData
{
    internal interface IDelete
    {
        IKey Key { get; }
        IWriteConsistency Consistency { get; }
    }

    public class Delete<T> : IDelete, ICommand<T> where T : IReplicatedData
    {
        readonly Key<T> _key;
        readonly IWriteConsistency _consistency;

        public Key<T> Key
        {
            get { return _key; }
        }

        public IWriteConsistency Consistency
        {
            get { return _consistency; }
        }

        public Delete(Key<T> key, IWriteConsistency consistency)
        {
            _key = key;
            _consistency = consistency;
        }

        IKey<T> ICommand<T>.Key
        {
            get { return _key; }
        }

        IKey IDelete.Key
        {
            get { return _key; }
        }
    }

    public interface IDeleteResponse<T> where T : IReplicatedData
    {
        Key<T> Key { get; }
    }

    public class DeleteSuccess<T> : IDeleteResponse<T> where T : IReplicatedData
    {
        readonly Key<T> _key;

        public Key<T> Key
        {
            get { return _key; }
        }

        public DeleteSuccess(Key<T> key)
        {
            _key = key;
        }
    }

    public class ReplicationDeletedFailure<T> : IDeleteResponse<T> where T : IReplicatedData
    {
        readonly Key<T> _key;

        public Key<T> Key
        {
            get { return _key; }
        }

        public ReplicationDeletedFailure(Key<T> key)
        {
            _key = key;
        }
    }

    public class DataDeleted<T> : Exception, IDeleteResponse<T> where T : IReplicatedData
    {
        readonly Key<T> _key;

        public Key<T> Key
        {
            get { return _key; }
        }

        public DataDeleted(Key<T> key)
        {
            _key = key;
        }

        public override string ToString()
        {
 	        return String.Format("DataDeleted {0}", _key.Id);
        }
    }
}
