using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Akka.TestKit.Internals
{
    /// <summary>
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public class BlockingCollectionTestActorQueue<T> : ITestActorQueue<T>
    {
        private readonly BlockingCollection<T> _collection;

        public BlockingCollectionTestActorQueue(BlockingCollection<T> collection)
        {
            _collection = collection;
        }

        public void Add(T item)
        {
            _collection.Add(item);
        }

        public IEnumerable<T> GetAll()
        {
            T item;
            while(_collection.TryTake(out item))
            {
                yield return item;
            }
        }
    }
}