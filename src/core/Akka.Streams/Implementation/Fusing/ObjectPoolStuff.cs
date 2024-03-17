// -----------------------------------------------------------------------
//  <copyright file="ObjectPoolStuff.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Akka.Annotations;

namespace Akka.Streams.Implementation.Fusing;

/// <remarks>
/// This is an object pool based on 
/// </remarks>
[InternalApi]
internal sealed class ObjectPoolStuff
{
    /*
    internal interface IObjectPoolNode<T>
    {
        ref T? NextNode { get; }
    }

    internal sealed class ObjectPool
    {
        private static int typeId = -1; // Increment by IdentityGenerator<T>

        private readonly object _gate = new object();
        private readonly int _poolLimit;
        private object[] _poolNodes = new object[4]; // ObjectPool<T>[]

        // pool-limit per type.
        public ObjectPool(int poolLimit)
        {
            _poolLimit = poolLimit;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryRent<T>([NotNullWhen(true)] out T? value)
            where T : class, IObjectPoolNode<T>
        {
            // poolNodes is grow only, safe to access indexer with no-lock
            var id = IdentityGenerator<T>.Identity;
            if (id < _poolNodes.Length &&
                _poolNodes[id] is ObjectPool<T> pool)
            {
                return pool.TryPop(out value);
            }

            Grow<T>(id);
            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Return<T>(T value)
            where T : class, IObjectPoolNode<T>
        {
            var id = IdentityGenerator<T>.Identity;
            if (id < _poolNodes.Length &&
                _poolNodes[id] is ObjectPool<T> pool)
            {
                return pool.TryPush(value);
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Grow<T>(int id)
            where T : class, IObjectPoolNode<T>
        {
            lock (_gate)
            {
                if (_poolNodes.Length <= id)
                {
                    Array.Resize(ref _poolNodes,
                        Math.Max(_poolNodes.Length * 2, id + 1));
                    _poolNodes[id] = new ObjectPool<T>(_poolLimit);
                }
                else if (_poolNodes[id] == null)
                {
                    _poolNodes[id] = new ObjectPool<T>(_poolLimit);
                }
                else
                {
                    // other thread already created new ObjectPool<T> so do nothing.
                }
            }
        }

        // avoid for Dictionary<Type, ***> lookup cost.
        private static class IdentityGenerator<T>
        {
#pragma warning disable SA1401
            public static int Identity;
#pragma warning restore SA1401

            static IdentityGenerator()
            {
                Identity = Interlocked.Increment(ref typeId);
            }
        }
    }
*/

    internal sealed class ObjectPoolV2<T>
    {
        private readonly int _limit;
        private readonly ConcurrentQueue<T> _items = new();
        //private readonly decimal _softLimit = default;
        //private readonly decimal _softLimit2 = default;
        //private readonly decimal _softLimit3 = default;
        //private readonly decimal _softLimit4 = default;
        private int _size;
        //private readonly decimal _hardLimit1 = default;
        //private readonly decimal _hardLimit2 = default;
        //private readonly decimal _hardLimit3 = default;
        //private readonly decimal _hardLimit4 = default;
        public ObjectPoolV2(int size)
        {
            _limit = size+3;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryPop([NotNullWhen(true)] out T? result)
        {
            // Instead of lock, use CompareExchange gate.
            // In a worst case, missed cached object(create new one) but it's not a big deal.
            if (_items.TryDequeue(out result))
            {
                Interlocked.Decrement(ref _size);
                return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void TryPush(T item)
        {
            if (Interlocked.Increment(ref _size) <= _limit)
            {
                _items.Enqueue(item);
            }
            else
            {
                Interlocked.Decrement(ref _size);
            }
        }
    }
    
    /*
    internal sealed class ObjectPool<T>
        where T : class, IObjectPoolNode<T>
    {
        private readonly int _limit;
        private int _gate;
        private int _size;
        private T? _root;

        public ObjectPool(int limit)
        {
            _limit = limit;
        }

        public int Size => _size;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryPop([NotNullWhen(true)] out T? result)
        {
            // Instead of lock, use CompareExchange gate.
            // In a worst case, missed cached object(create new one) but it's not a big deal.
            if (Interlocked.CompareExchange(ref _gate, 1, 0) == 0)
            {
                var v = _root;
                if (!(v is null))
                {
                    ref var nextNode = ref v.NextNode;
                    _root = nextNode;
                    nextNode = null;
                    _size--;
                    result = v;
                    Volatile.Write(ref _gate, 0);
                    return true;
                }

                Volatile.Write(ref _gate, 0);
            }

            result = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryPush(T item)
        {
            if (Interlocked.CompareExchange(ref _gate, 1, 0) == 0)
            {
                if (_size < _limit)
                {
                    item.NextNode = _root;
                    _root = item;
                    _size++;
                    Volatile.Write(ref _gate, 0);
                    return true;
                }
                else
                {
                    Volatile.Write(ref _gate, 0);
                }
            }

            return false;
        }
    }
    */
}