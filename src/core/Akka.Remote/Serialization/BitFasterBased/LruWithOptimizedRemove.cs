// //-----------------------------------------------------------------------
// // <copyright file="LruWithOptimizedRemove.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BitFaster.Caching;
using BitFaster.Caching.Lru;

namespace Akka.Remote.Serialization.BitFasterBased
{
    public interface IPolicy<in K, in V, I> where I : LruItem<K, V>
    {
        I CreateItem(K key, V value);

        void Touch(I item);

        bool ShouldDiscard(I item);

        ItemDestination RouteHot(I item);

        ItemDestination RouteWarm(I item);

        ItemDestination RouteCold(I item);
    }
    /// <summary>
    /// Discards the least recently used items first. 
    /// </summary>
    public readonly struct LruPolicy<K, V> : IPolicy<K, V, LruItem<K, V>>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LruItem<K, V> CreateItem(K key, V value)
        {
            return new LruItem<K, V>(key, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Touch(LruItem<K, V> item)
        {
            item.SetAccessed();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ShouldDiscard(LruItem<K, V> item)
        {
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ItemDestination RouteHot(LruItem<K, V> item)
        {
            if (item.WasAccessed)
            {
                return ItemDestination.Warm;
            }

            return ItemDestination.Cold;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ItemDestination RouteWarm(LruItem<K, V> item)
        {
            if (item.WasAccessed)
            {
                return ItemDestination.Warm;
            }

            return ItemDestination.Cold;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ItemDestination RouteCold(LruItem<K, V> item)
        {
            if (item.WasAccessed)
            {
                return ItemDestination.Warm;
            }

            return ItemDestination.Remove;
        }
    }
    
    [Flags]
    public enum LruItemStatus
    {
        WasRemoved = 1,
        WasAccessed = 2,
        ShouldToss = 4
    }
    public class LruItem<K, V>
    {
        private volatile LruItemStatus _status;
        //private volatile bool wasAccessed;
        //private volatile bool wasRemoved;

        public LruItem(K k, V v)
        {
            this.Key = k;
            this.Value = v;
        }

        public readonly K Key;

        public V Value { get; set; }

        public bool WasAccessed
        {
            get => (this._status & LruItemStatus.WasAccessed) == LruItemStatus.WasAccessed;
            //set => this._status = this._status & LruItemStatus.WasAccessed;
        }

        public void SetAccessed()
        {
            this._status = this._status & LruItemStatus.WasAccessed;
        }

        public void SetUnaccessed()
        {
            this._status = (this._status & (LruItemStatus)int.MaxValue -
                (int)LruItemStatus.WasAccessed);
        }

        public bool WasRemoved
        {
            get => (this._status & LruItemStatus.WasRemoved) == LruItemStatus.WasRemoved;
            
        }

        public void SetRemoved()
        {
            this._status = this._status & LruItemStatus.WasRemoved;
        }

        public bool ShouldFastDiscard
        {
            get => (this._status & LruItemStatus.ShouldToss) ==
                   LruItemStatus.ShouldToss;
            set => this._status = this._status & LruItemStatus.ShouldToss;
        }
    }
    ///<inheritdoc/>
    public sealed class FastConcurrentLru<K, V> : TemplateConcurrentLru<K, V, NullHitCounter>
    {
        /// <summary>
        /// Initializes a new instance of the FastConcurrentLru class with the specified capacity that has the default 
        /// concurrency level, and uses the default comparer for the key type.
        /// </summary>
        /// <param name="capacity">The maximum number of elements that the FastConcurrentLru can contain.</param>
        public FastConcurrentLru(int capacity)
            : base(Environment.ProcessorCount, capacity, EqualityComparer<K>.Default, new LruPolicy<K, V>(), new NullHitCounter())
        {
        }

        /// <summary>
        /// Initializes a new instance of the FastConcurrentLru class that has the specified concurrency level, has the 
        /// specified initial capacity, and uses the specified IEqualityComparer<T>.
        /// </summary>
        /// <param name="concurrencyLevel">The estimated number of threads that will update the FastConcurrentLru concurrently.</param>
        /// <param name="capacity">The maximum number of elements that the FastConcurrentLru can contain.</param>
        /// <param name="comparer">The IEqualityComparer<T> implementation to use when comparing keys.</param>
        public FastConcurrentLru(int concurrencyLevel, int capacity, IEqualityComparer<K> comparer)
            : base(concurrencyLevel, capacity, comparer, new LruPolicy<K, V>(), new NullHitCounter())
        {
        }
    }
    /// <summary>
    /// Pseudo LRU implementation where LRU list is composed of 3 segments: hot, warm and cold. Cost of maintaining
    /// segments is amortized across requests. Items are only cycled when capacity is exceeded. Pure read does
    /// not cycle items if all segments are within capacity constraints.
    /// There are no global locks. On cache miss, a new item is added. Tail items in each segment are dequeued,
    /// examined, and are either enqueued or discarded.
    /// This scheme of hot, warm and cold is based on the implementation used in MemCached described online here:
    /// https://memcached.org/blog/modern-lru/
    /// </summary>
    /// <remarks>
    /// Each segment has a capacity. When segment capacity is exceeded, items are moved as follows:
    /// 1. New items are added to hot, WasAccessed = false
    /// 2. When items are accessed, update WasAccessed = true
    /// 3. When items are moved WasAccessed is set to false.
    /// 4. When hot is full, hot tail is moved to either Warm or Cold depending on WasAccessed. 
    /// 5. When warm is full, warm tail is moved to warm head or cold depending on WasAccessed.
    /// 6. When cold is full, cold tail is moved to warm head or removed from dictionary on depending on WasAccessed.
    /// </remarks>
    public class TemplateConcurrentLru<K, V, H> : ICache<K, V>
        where H : struct, IHitCounter
    {
        private readonly ConcurrentDictionary<K, LruItem<K,V>> dictionary;

        private readonly ConcurrentQueue<LruItem<K,V>> hotQueue;
        private readonly ConcurrentQueue<LruItem<K,V>> warmQueue;
        private readonly ConcurrentQueue<LruItem<K,V>> coldQueue;

        // maintain count outside ConcurrentQueue, since ConcurrentQueue.Count holds a global lock
        private int hotCount;
        private int warmCount;
        private int coldCount;

        private readonly int hotCapacity;
        private readonly int warmCapacity;
        private readonly int coldCapacity;

        private readonly LruPolicy<K,V> policy;

        // Since H is a struct, making it readonly will force the runtime to make defensive copies
        // if mutate methods are called. Therefore, field must be mutable to maintain count.
        protected H hitCounter;

        public TemplateConcurrentLru(
            int concurrencyLevel,
            int capacity,
            IEqualityComparer<K> comparer,
            LruPolicy<K,V> itemPolicy,
            H hitCounter)
        {
            if (capacity < 3)
            {
                throw new ArgumentOutOfRangeException("Capacity must be greater than or equal to 3.");
            }

            if (comparer == null)
            {
                throw new ArgumentNullException(nameof(comparer));
            }

            var queueCapacity = ComputeQueueCapacity(capacity);
            this.hotCapacity = queueCapacity.hot;
            this.warmCapacity = queueCapacity.warm;
            this.coldCapacity = queueCapacity.cold;

            this.hotQueue = new ConcurrentQueue<LruItem<K,V>>();
            this.warmQueue = new ConcurrentQueue<LruItem<K,V>>();
            this.coldQueue = new ConcurrentQueue<LruItem<K,V>>();

            int dictionaryCapacity = this.hotCapacity + this.warmCapacity + this.coldCapacity + 1;

            this.dictionary = new ConcurrentDictionary<K, LruItem<K,V>>(concurrencyLevel, dictionaryCapacity, comparer);
            this.policy = itemPolicy;
            this.hitCounter = hitCounter;
        }

        // No lock count: https://arbel.net/2013/02/03/best-practices-for-using-concurrentdictionary/
        public int Count => this.dictionary.Skip(0).Count();

        public int HotCount => this.hotCount;

        public int WarmCount => this.warmCount;

        public int ColdCount => this.coldCount;

        ///<inheritdoc/>
        public bool TryGet(K key, out V value)
        {
            LruItem<K,V> item;
            if (dictionary.TryGetValue(key, out item))
            {
                return GetOrDiscard(item, out value);
            }

            value = default(V);
            this.hitCounter.IncrementMiss();
            return false;
        }
        
        public bool TryPullLazy(K key, out V value)
        {
            if (this.dictionary.TryGetValue(key, out var existing))
            {
                bool retVal = GetOrDiscard(existing, out value);
                //existing.WasAccessed = false;
                existing.SetRemoved();
                existing.ShouldFastDiscard = true;
                // serialize dispose (common case dispose not thread safe)
                //if (existing.Value is IDisposable)
                //{
                //    lock (existing)
                //    {
                //        if (existing.Value is IDisposable d)
                //        {
                //            d.Dispose();
                //        }
                //    }    
                //}

                return retVal;
            }

            value = default(V);
            return false;
        }

        // AggressiveInlining forces the JIT to inline policy.ShouldDiscard(). For LRU policy 
        // the first branch is completely eliminated due to JIT time constant propogation.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool GetOrDiscard(LruItem<K,V> item, out V value)
        {
            if (this.policy.ShouldDiscard(item))
            {
                this.Move(item, ItemDestination.Remove);
                this.hitCounter.IncrementMiss();
                value = default(V);
                return false;
            }

            value = item.Value;
            this.policy.Touch(item);
            this.hitCounter.IncrementHit();
            return true;
        }

        ///<inheritdoc/>
        public V GetOrAdd(K key, Func<K, V> valueFactory)
        {
            if (this.TryGet(key, out var value))
            {
                return value;
            }

            // The value factory may be called concurrently for the same key, but the first write to the dictionary wins.
            // This is identical logic in ConcurrentDictionary.GetOrAdd method.
            var newItem = this.policy.CreateItem(key, valueFactory(key));

            if (this.dictionary.TryAdd(key, newItem))
            {
                this.hotQueue.Enqueue(newItem);
                Interlocked.Increment(ref hotCount);
                Cycle();
                return newItem.Value;
            }

            return this.GetOrAdd(key, valueFactory);
        }

        ///<inheritdoc/>
        public async Task<V> GetOrAddAsync(K key, Func<K, Task<V>> valueFactory)
        {
            if (this.TryGet(key, out var value))
            {
                return value;
            }

            // The value factory may be called concurrently for the same key, but the first write to the dictionary wins.
            // This is identical logic in ConcurrentDictionary.GetOrAdd method.
            var newItem = this.policy.CreateItem(key, await valueFactory(key).ConfigureAwait(false));

            if (this.dictionary.TryAdd(key, newItem))
            {
                this.hotQueue.Enqueue(newItem);
                Interlocked.Increment(ref hotCount);
                Cycle();
                return newItem.Value;
            }

            return await this.GetOrAddAsync(key, valueFactory).ConfigureAwait(false);
        }

        

        ///<inheritdoc/>
        public bool TryRemove(K key)
        {
            if (this.dictionary.TryGetValue(key, out var existing))
            {
                var kvp = new KeyValuePair<K, LruItem<K,V>>(key, existing);

                // hidden atomic remove
                // https://devblogs.microsoft.com/pfxteam/little-known-gems-atomic-conditional-removals-from-concurrentdictionary/
                if (((ICollection<KeyValuePair<K, LruItem<K,V>>>)this.dictionary).Remove(kvp))
                {
                    // Mark as not accessed, it will later be cycled out of the queues because it can never be fetched 
                    // from the dictionary. Note: Hot/Warm/Cold count will reflect the removed item until it is cycled 
                    // from the queue.
                    //existing.WasAccessed = false;
                    existing.SetUnaccessed();
                    existing.SetRemoved();
                    //existing.WasRemoved = true;

                    // serialize dispose (common case dispose not thread safe)
                    //if (existing.Value is IDisposable)
                    //{
                    //    lock (existing)
                    //    {
                    //        if (existing.Value is IDisposable d)
                    //        {
                    //            d.Dispose();
                    //        }
                    //    }    
                    //}
                    

                    return true;
                }

                // it existed, but we couldn't remove - this means value was replaced afer the TryGetValue (a race), try again
                return TryRemove(key);
            }

            return false;
        }

        ///<inheritdoc/>
        ///<remarks>Note: Calling this method does not affect LRU order.</remarks>
        public bool TryUpdate(K key, V value)
        {
            if (this.dictionary.TryGetValue(key, out var existing))
            {
                lock (existing)
                {
                    if (!existing.WasRemoved)
                    {
                        V oldValue = existing.Value;
                        existing.Value = value;

                        //if (oldValue is IDisposable d)
                        //{
                        //    d.Dispose();
                        //}

                        return true;
                    }
                }
            }

            return false;
        }

        public void TryAdd(K key, V value)
        {
            var newItem = this.policy.CreateItem(key, value);
            if (this.dictionary.TryAdd(key, newItem))
            {
                this.hotQueue.Enqueue(newItem);
                Interlocked.Increment(ref hotCount);
                Cycle();
                return;
            }
        }

        ///<inheritdoc/>
        ///<remarks>Note: Updates to existing items do not affect LRU order. Added items are at the top of the LRU.</remarks>
        public void AddOrUpdate(K key, V value)
        { 
            // first, try to update
            if (this.TryUpdate(key, value))
            { 
                return;
            }

            // then try add
            var newItem = this.policy.CreateItem(key, value);

            if (this.dictionary.TryAdd(key, newItem))
            {
                this.hotQueue.Enqueue(newItem);
                Interlocked.Increment(ref hotCount);
                Cycle();
                return;
            }

            // if both update and add failed there was a race, try again
            AddOrUpdate(key, value);
        }

        ///<inheritdoc/>
        public void Clear()
        {
            // take a key snapshot
            var keys = this.dictionary.Keys.ToList();

            // remove all keys in the snapshot - this correctly handles disposable values
            foreach (var key in keys)
            {
                TryRemove(key);
            }

            // At this point, dictionary is empty but queues still hold references to all values.
            // Cycle the queues to purge all refs. If any items were added during this process, 
            // it is possible they might be removed as part of CycleCold. However, the dictionary
            // and queues will remain in a consistent state.
            for (int i = 0; i < keys.Count; i++)
            {
                CycleHotUnchecked();
                CycleWarmUnchecked();
                CycleColdUnchecked();
            }
        }

        private void Cycle()
        {
            // There will be races when queue count == queue capacity. Two threads may each dequeue items.
            // This will prematurely free slots for the next caller. Each thread will still only cycle at most 5 items.
            // Since TryDequeue is thread safe, only 1 thread can dequeue each item. Thus counts and queue state will always
            // converge on correct over time.
            CycleHot();

            // Multi-threaded stress tests show that due to races, the warm and cold count can increase beyond capacity when
            // hit rate is very high. Double cycle results in stable count under all conditions. When contention is low, 
            // secondary cycles have no effect.
            CycleWarm();
            CycleWarm();
            CycleCold();
            CycleCold();
        }

        private void CycleHot()
        {
            if (this.hotCount > this.hotCapacity)
            {
                CycleHotUnchecked();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CycleHotUnchecked()
        {
            Interlocked.Decrement(ref this.hotCount);

            if (this.hotQueue.TryDequeue(out var item))
            {
                var where = this.policy.RouteHot(item);
                this.Move(item, where);
            }
            else
            {
                Interlocked.Increment(ref this.hotCount);
            }
        }

        private void CycleWarm()
        {
            if (this.warmCount > this.warmCapacity)
            {
                CycleWarmUnchecked();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CycleWarmUnchecked()
        {
            Interlocked.Decrement(ref this.warmCount);

            if (this.warmQueue.TryDequeue(out var item))
            {
                var where = this.policy.RouteWarm(item);

                // When the warm queue is full, we allow an overflow of 1 item before redirecting warm items to cold.
                // This only happens when hit rate is high, in which case we can consider all items relatively equal in
                // terms of which was least recently used.
                if (where == ItemDestination.Warm && this.warmCount <= this.warmCapacity)
                {
                    this.Move(item, where);
                }
                else
                {
                    this.Move(item, ItemDestination.Cold);
                }
            }
            else
            {
                Interlocked.Increment(ref this.warmCount);
            }
        }

        private void CycleCold()
        {
            if (this.coldCount > this.coldCapacity)
            {
                CycleColdUnchecked();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CycleColdUnchecked()
        {
            Interlocked.Decrement(ref this.coldCount);

            if (this.coldQueue.TryDequeue(out var item))
            {
                var where = this.policy.RouteCold(item);

                if (where == ItemDestination.Warm && this.warmCount <= this.warmCapacity)
                {
                    this.Move(item, where);
                }
                else
                {
                    this.Move(item, ItemDestination.Remove);
                }
            }
            else
            {
                Interlocked.Increment(ref this.coldCount);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Move(LruItem<K,V> item, ItemDestination where)
        {
            item.SetUnaccessed();
            if (item.WasRemoved)
                return;
            switch (where)
            {
                case ItemDestination.Warm:
                    this.warmQueue.Enqueue(item);
                    Interlocked.Increment(ref this.warmCount);
                    break;
                case ItemDestination.Cold:
                    this.coldQueue.Enqueue(item);
                    Interlocked.Increment(ref this.coldCount);
                    break;
                case ItemDestination.Remove:
                    if (item.ShouldFastDiscard == false)
                    {

                        var kvp =
                            new KeyValuePair<K, LruItem<K, V>>(item.Key, item);

                        // hidden atomic remove
                        // https://devblogs.microsoft.com/pfxteam/little-known-gems-atomic-conditional-removals-from-concurrentdictionary/
                        if (((ICollection<KeyValuePair<K, LruItem<K, V>>>)this
                            .dictionary).Remove(kvp))
                        {
                            item.SetRemoved();
                            //item.WasRemoved = true;

                            //if (item.Value is IDisposable)
                            //{
                            //    lock (item)
                            //    {
                            //        if (item.Value is IDisposable d)
                            //        {
                            //            d.Dispose();
                            //        }
                            //    }
                            //}
                        }

                    }
                    else
                    {
                        this.dictionary.TryRemove(item.Key, out var trash);
                    }

                    break;
            }
        }

        private static (int hot, int warm, int cold) ComputeQueueCapacity(int capacity)
        {
            int hotCapacity = capacity / 3;
            int warmCapacity = capacity / 3;
            int coldCapacity = capacity / 3;

            int remainder = capacity % 3;

            switch (remainder)
            {
                case 1:
                    coldCapacity++;
                    break;
                case 2:
                    hotCapacity++;
                    coldCapacity++;
                    break;
            }

            return (hotCapacity, warmCapacity, coldCapacity);
        }
    }
}

