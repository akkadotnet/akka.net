using System;
using System.Collections;
using System.Collections.Generic;
using Akka.Remote.Artery.Internal;
using Akka.Util;

namespace Akka.Remote.Artery.Compress
{
    /// <summary>
    /// INTERNAL API
    ///
    /// Mutable, open-addressing with linear-probing (though naive one which in theory could get pathological)
    /// heavily optimized "top N heavy hitters" data-structure.
    ///
    /// Keeps a number of specific heavy hitters around in memory.
    ///
    /// See also Section 5.2 of http://dimacs.rutgers.edu/~graham/pubs/papers/cm-full.pdf
    /// for a discussion about the assumptions made and guarantees about the Heavy Hitters made in this model.
    /// We assume the Cash Register model in which there are only additions, which simplifies HH detection significantly.
    ///
    /// This class is a hybrid data structure containing a hash map and a heap pointing to slots in the hash map. The capacity
    /// of the hash map is twice that of the heap to reduce clumping of entries on collisions.
    /// </summary>
    internal class TopHeavyHitters<T> : IEnumerable<T> where T : class
    {
        private readonly int _max;
        private readonly int _adjustedMax;
        // Contains the hash value for each entry in the hash map. Used for quicker lookups (equality check can be avoided
        // if hashes don't match)
        private readonly int[] _hashes;
        // Actual stored elements in the hash map
        private readonly T[] _items;
        // Index of stored element in the associated heap
        private readonly int[] _heapIndex;
        // Weights associated with an entry in the hash map. Used to maintain the heap property and give easy access to low
        // weight entries
        private readonly long[] _weights;

        // Heap structure containing indices to slots in the hash map
        private readonly int[] _heap;

        public int Capacity { get; }
        public int Mask { get; }

        public TopHeavyHitters(int max)
        {
            _max = max;
            _adjustedMax = max == 0 ? 1 : max;

            _adjustedMax.Requiring(m => (m & (m - 1)) == 0,
                "Maximum numbers of heavy hitters should be in form of 2^k for any natural k");

            Capacity = _adjustedMax * 2;
            Mask = Capacity - 1;

            _hashes = new int[Capacity];
            _items = new T[Capacity];

            _heapIndex = new int[Capacity];
            _heapIndex.AsSpan().Fill(-1);

            _weights = new long[Capacity];

            _heap = new int[_adjustedMax];
            _heap.AsSpan().Fill(-1);
        }

        /*
         * Invariants (apart from heap and hash map invariants):
         * - Heap is ordered by weights in an increasing order, so lowest weight is at the top of the heap (heap(0))
         * - Each heap slot contains the index of the hash map slot where the actual element is, or contains -1
         * - Empty heap slots contain a negative index
         * - Each hash map slot should contain the index of the heap slot where the element currently is
         * - Initially the heap is filled with "pseudo" elements (-1 entries with weight 0). This ensures that when the
         *   heap is not full a "lowestHitterWeight" of zero is reported. This also ensures that we can always safely
         *   remove the "lowest hitter" without checking the capacity, as when the heap is not full it will contain a
         *   "pseudo" element as lowest hitter which can be safely removed without affecting real entries.
         */

        /*
         * Insertion of a new element works like this:
         *  1. Check if its weight is larger than the lowest hitter we know. If the heap is not full, then due to the
         *    presence of "pseudo" elements (-1 entries in heap with weight 0) this will always succeed.
         *  2. Do a quick scan for a matching hashcode with findHashIdx. This only searches for the first candidate (one
         *    with equal hash).
         *    a. If this returns -1, we know this is a new entry (there is no other entry with matching hashcode).
         *    b. Else, this returns a nonnegative number. Now we do a more thorough scan from here, actually checking for
         *       object equality by calling findItemIdx (discarding false positives due to hash collisions). Since this
         *       is a new entry, the result will be -1 (the candidate was a colliding false positive).
         *  3. Call insertKnownHeavy
         *  4. This removes the lowest hitter first from the hash map.
         *    a. If the heap is not full, this will be just a "pseudo" element (-1 entry with weight 0) so no "real" entries
         *       are evicted.
         *    b. If the heap is full, then a real entry is evicted. This is correct as the new entry has higher weight than
         *       the evicted one due to the check in step 1.
         *  5. The new entry is inserted into the hash map and its entry index is recorded
         *  6. Overwrite the top of the heap with inserted element's index (again, correct, this was the evicted real or
         *     "pseudo" element).
         *  7. Restore heap property by pushing down the new element from the top if necessary.
         */

        /*
         * Update of an existing element works like this:
         * 1. Check if its weight is larger than the lowest hitter we know. This will succeed as weights can only be
         *    incremented.
         * 2. Do a quick scan for a matching hashcode with findHashIdx. This only searches for the first candidate (one
         *    with equal hash). This does not check for actual entry equality yet, but it can quickly skip surely non-matching
         *    entries. Since this is an existing element, we will find an index that is a candidate for actual equality.
         * 3. Now do a more thorough scan from this candidate index checking for hash *and* object equality (to skip
         *    potentially colliding entries). We will now have the real index of the existing entry if the previous scan
         *    found a false positive.
         * 4. Call updateExistingHeavyHitter
         * 5. Update the recorded weight for this entry in the hash map (at the index we previously found)
         * 6. Fix the Heap property (since now weight can be larger than one of its heap children nodes). Please note that
         *    we just swap heap entries around here, so no entry will be evicted.
         */

        /// <summary>
        /// Iterates over the current heavy hitters, order is not of significance.
        /// Not thread safe, accesses internal heap directly (to avoid copying of data). Access must be synchronized externally.
        /// </summary>
        /// <returns></returns>
        public IEnumerator<T> GetEnumerator()
            => new TopHeavyHitterEnumerator(this);

        public string ToDebugString
            => $@"TopHeavyHitters(
  max: {_max}
  lowestHitterIdx: {LowestHitterIndex} (weight: {LowestHitterWeight})

  hashes:     [{string.Join(", ", _hashes)}]
  weights:    [{string.Join(", ", _weights)}]
  items:      [{string.Join(", ", _items.ToString())}]
  heapIndex:  [{string.Join(", ", _heapIndex)}]
  heap:       [{string.Join(", ", _heap)}]
)";

        /// <summary>
        /// <para>
        /// Attempt adding item to heavy hitters set, if it does not fit in the top yet,
        /// it will be dropped and the method will return `false`.
        /// </para>
        /// <para>
        /// WARNING: It is only allowed to *increase* the weight of existing elements, decreasing is disallowed.
        /// </para>
        /// </summary>
        /// <param name="item"></param>
        /// <param name="count"></param>
        /// <returns>`true` if the added item has become a heavy hitter.</returns>
        public bool Update(T item, long count)
        {
            if (IsHeavy(count)) // O(1) terminate execution ASAP if known to not be a heavy hitter anyway
                return true;

            var hashCode = item.GetHashCode();
            var startIndex = hashCode & Mask;

            // We first try to find the slot where an element with an equal hash value is. This is a possible candidate
            // for an actual matching entry (unless it is an entry with a colliding hash value).
            // worst case O(n), common O(1 + alpha), can't really bin search here since indexes are kept in synch with other arrays hmm...
            var candidateIndex = FindHashIdx(startIndex, hashCode);

            if (candidateIndex == -1)
            {
                // No matching hash value entry is found, so we are sure we don't have this entry yet.
                InsertKnownNewHeavy(hashCode, item, count); // O(log n + alpha)
                return true;
            }

            // We now found, relatively cheaply, the first index where our searched entry *might* be (hashes are equal).
            // This is not guaranteed to be the one we are searching for, yet (hash values may collide).
            // From this position we can invoke the more costly search which checks actual object equalities.
            // With this two step search we avoid equality checks completely for many non-colliding entries.
            var actualIdx = FindItemIdx(candidateIndex, hashCode, item);

            // usually O(1), worst case O(n) if we need to scan due to hash conflicts
            if (actualIdx == -1)
            {
                // So we don't have this entry so far (only a colliding one, it was a false positive from findHashIdx).
                InsertKnownNewHeavy(hashCode, item, count); // O(1 + log n), we simply replace the current lowest heavy hitter
                return true;
            }

            // The entry exists, let's update it.
            UpdateExistingHeavyHitter(actualIdx, count);
            // not a "new" heavy hitter, since we only replaced it (so it was signaled as new once before)
            return false;
        }

        /// <summary>
        /// Checks the lowest weight entry in this structure and returns true if the given count is larger than that.
        /// In other words this checks if a new entry can be added as it is larger than the known least weight.
        /// </summary>
        /// <param name="count"></param>
        /// <returns></returns>
        private bool IsHeavy(long count)
            => count > LowestHitterWeight;

        /// <summary>
        /// Finds the index of an element in the hashtable (or returns -1 if it is not found).
        /// It is usually a good idea to find the first eligible index with findHashIdx before
        /// invoking this method since that finds the first eligible candidate (one with an equal
        /// hash value) faster than this method (since this checks actual object equality).
        /// </summary>
        /// <param name="searchFromIndex"></param>
        /// <param name="hashCode"></param>
        /// <param name="o"></param>
        /// <returns></returns>
        private int FindItemIdx(int searchFromIndex, int hashCode, T o)
        {
            if (searchFromIndex == -1) return -1;
            if (_items[searchFromIndex].Equals(o)) return searchFromIndex;

            var index = (searchFromIndex + 1) & Mask;
            while (true)
            {
                // Scanned the whole table, returned to the start => element not in table
                if (index == searchFromIndex) return -1;

                if (hashCode == _hashes[index]) // First check on hashcode to avoid costly equality
                {
                    var item = _items[index];
                    if (item.Equals(o)) // Found the item, return its index
                        return index;

                    // Item did not match, continue probing.
                    // TODO: This will probe the *whole* table all the time for non-entries, there is no option for early exit.
                    // TODO: Maybe record probe lengths so we can bail out early.
                    index = (index + 1) & Mask;
                    continue;
                }

                // hashcode did not match the one in slot, move to next index (linear probing)
                index = (index + 1) & Mask;
            }
        }

        /// <summary>
        /// Replace existing heavy hitter – give it a new `count` value. This will also restore the heap property, so this
        /// might make a previously lowest hitter no longer be one.
        /// </summary>
        /// <param name="foundHashIndex"></param>
        /// <param name="count"></param>
        private void UpdateExistingHeavyHitter(int foundHashIndex, long count)
        {
            if(_weights[foundHashIndex] > count)
                throw new ArgumentException(
                    "Weights can be only incremented or kept the same, not decremented. " +
                    $"Previous weight was [{_weights[foundHashIndex]}], attempted to modify it to [{count}].");
            _weights[foundHashIndex] = count; // we don't need to change `hashCode`, `heapIndex` or `item`, those remain the same
            // Position in the heap might have changed as count was incremented
            FixHeap(_heapIndex[foundHashIndex]);
        }

        /// <summary>
        /// Search in the hash map the first slot that contains an equal hashcode to the one we search for.
        /// This is an optimization: before we start to compare elements, we find the first eligible
        /// candidate (whose hash map matches the one we search for).
        /// From this index usually findItemIndex is called which does further probing, but checking actual element equality.
        /// </summary>
        /// <param name="searchFromIndex"></param>
        /// <param name="hashCode"></param>
        /// <returns></returns>
        private int FindHashIdx(int searchFromIndex, int hashCode)
        {
            var i = 0;
            while (i < _hashes.Length)
            {
                var index = (i + searchFromIndex) & Mask;
                if (_hashes[index] == hashCode)
                    return index;
                i++;
            }

            return -1;
        }

        /// <summary>
        /// Call this if the weight of an entry at heap node `index` was incremented. The heap property says that
        /// "The key stored in each node is less than or equal to the keys in the node's children". Since we incremented
        /// (or kept the same) weight for this index, we only need to restore the heap "downwards", parents are not affected.
        /// </summary>
        /// <param name="index"></param>
        private void FixHeap(int index)
        {
            while (true)
            {
                var leftIndex = index * 2 + 1;
                var rightIndex = index * 2 + 2;
                var currentWeight = _weights[_heap[index]];
                if (rightIndex < _adjustedMax)
                {
                    var leftValueIndex = _heap[leftIndex];
                    var rightValueIndex = _heap[rightIndex];
                    if (leftValueIndex < 0)
                    {
                        SwapHeapNode(index, leftIndex);
                        index = leftIndex;
                        continue;
                    }
                    else if (rightValueIndex < 0)
                    {
                        SwapHeapNode(index, rightIndex);
                        index = rightIndex;
                        continue;
                    }
                    else
                    {
                        var rightWeight = _weights[rightValueIndex];
                        var leftWeight = _weights[leftValueIndex];
                        if (leftWeight < rightWeight)
                        {
                            if (currentWeight > leftWeight)
                            {
                                SwapHeapNode(index, leftIndex);
                                index = leftIndex;
                                continue;
                            }
                        }
                        else
                        {
                            if (currentWeight > rightWeight)
                            {
                                SwapHeapNode(index, rightIndex);
                                index = rightIndex;
                                continue;
                            }
                        }
                    }
                }
                else if (leftIndex < _adjustedMax)
                {
                    var leftValueIndex = _heap[leftIndex];
                    if (leftValueIndex < 0)
                    {
                        SwapHeapNode(index, leftIndex);
                        index = leftIndex;
                        continue;
                    }
                    else
                    {
                        var leftWeights = _weights[leftValueIndex];
                        if (currentWeight > leftWeights)
                        {
                            SwapHeapNode(index, leftIndex);
                            index = leftIndex;
                            continue;
                        }
                    }
                }

                break;
            }
        }

        /// <summary>
        /// Swaps two elements in `heap` array and maintain correct index in `heapIndex`.
        /// </summary>
        /// <param name="a">index of first element</param>
        /// <param name="b">index of second element</param>
        private void SwapHeapNode(int a, int b)
        {
            if (_heap[a] >= 0)
                _heapIndex[_heap[a]] = b;

            if (_heap[b] >= 0)
                _heapIndex[_heap[b]] = a;

            var tmp = _heap[a];
            _heap[a] = _heap[b];
            _heap[b] = tmp;
        }

        /// <summary>
        /// Removes the current lowest hitter (can be a "pseudo" entry) from the hash map and inserts the new entry.
        /// Then it replaces the top of the heap (the removed entry) with the new entry and restores heap property.
        /// </summary>
        /// <param name="hashCode"></param>
        /// <param name="item"></param>
        /// <param name="count"></param>
        private void InsertKnownNewHeavy(int hashCode, T item, long count)
        {
            // Before we insert into the hash map, remove the lowest hitter. It is guaranteed that we have a `count`
            // larger than `lowestHitterWeight` here, so we can safely remove the lowest hitter. Note, that this might be a
            // "pseudo" element (-1 entry) in the heap if we are not at full capacity.
            RemoveHash(LowestHitterIndex);
            var hashTableIndex = Insert(hashCode, item, count);
            // Insert to the top of the heap.
            _heap[0] = hashTableIndex;
            _heapIndex[hashTableIndex] = 0;
            FixHeap(0);
        }

        /// <summary>
        /// Remove value from hash-table based on position.
        /// </summary>
        /// <param name="index"></param>
        private void RemoveHash(int index)
        {
            if (index < 0) 
                return;

            _items[index] = null;
            _heapIndex[index] = -1;
            _hashes[index] = 0;
            _weights[index] = 0;
        }

        /// <summary>
        /// <para>Insert value in hash-table.</para>
        /// <para>Using open addressing for resolving collisions.</para>
        /// <para>Initial index is reminder in division hashCode and table size.</para>
        /// </summary>
        /// <param name="hashCode">hashCode of item</param>
        /// <param name="item">value which should be added to hash-table</param>
        /// <param name="count">count associated to value</param>
        /// <returns>Index in hash-table where was inserted</returns>
        private int Insert(int hashCode, T item, long count)
        {
            var index = hashCode & Mask;
            while (_items[index] != null)
                index = (index + 1) & Mask;
            _hashes[index] = hashCode;
            _items[index] = item;
            _weights[index] = count;
            return index;
        }

        /// <summary>
        /// Weight of lowest heavy hitter, if a new inserted item has a weight greater than this, it is a heavy hitter.
        /// This gets the index of the lowest heavy hitter from the top of the heap (lowestHitterIndex) if there is any
        /// and looks up its weight.
        /// If there is no entry in the table (lowestHitterIndex returns a negative index) this returns a zero weight.
        /// </summary>
        private long LowestHitterWeight
        {
            get
            {
                var index = LowestHitterIndex;
                return index >= 0 ? _weights[index] : 0;
            }
        }

        private int LowestHitterIndex => _heap[0];

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private class TopHeavyHitterEnumerator : IEnumerator<T>
        {
            private readonly TopHeavyHitters<T> _parent;
            private int _i = 0;

            public TopHeavyHitterEnumerator(TopHeavyHitters<T> parent)
            {
                _parent = parent;
            }

            public bool MoveNext()
            {
                // note that this is using max and not adjustedMax so will be empty if disabled (max=0)
                var idx = _i;
                while (Value(idx) == null && idx < _parent._max)
                {
                    idx++;
                }

                if (idx >= _parent._max)
                    return false;

                Current = Value(_i);
                _i++;
                return true;
            }

            public void Reset() => _i = 0;

            private int Index(int i) => _parent._heap[i];
            private T Value(int i) => Index(i) < 0 ? null : _parent._items[Index(i)];

            public T Current { get; private set; }

            object IEnumerator.Current => Current;

            public void Dispose()
            { }
        }
    }
}
