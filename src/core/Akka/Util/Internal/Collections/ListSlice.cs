//-----------------------------------------------------------------------
// <copyright file="ListSlice.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Akka.Util.Internal.Collections
{
    internal static class SliceExtensions
    {
        public static IReadOnlyList<T> NoCopySlice<T>(this IReadOnlyList<T> list, int offset)
        {
            switch (list)
            {
                // slice of a slice
                case ListSlice<T> slice:
                {
                    return new ListSlice<T>(slice.Array, slice.Offset + offset, slice.Array.Count - slice.Offset - offset);
                }
                default:
                {
                    return new ListSlice<T>(list, offset, list.Count - offset);
                }
            }
        }
    }
    
    /// <summary>
    /// <see cref="ArraySegment{T}"/> but for <see cref="IReadOnlyList{T}"/>
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal struct ListSlice<T> : IList<T>, IReadOnlyList<T>
    {
        private sealed class SliceEnumerator : IEnumerator<T>
        {
            private readonly IReadOnlyList<T> _array;
            private readonly int _start;
            private readonly int _end;
            private int _current;

            public SliceEnumerator(ListSlice<T> array)
            {
                _array = array.Array;
                _start = array.Offset;
                _end = _start + array.Count;
                _current = _start - 1;
            }

            public bool MoveNext()
            {
                if (_current < _end)
                {
                    _current++;
                    return (_current < _end);
                }
                return false;
            }

            public void Reset()
            {
                _current = _start - 1;
            }

            public T Current
            {
                get
                {
                    if (_current < _start) throw new InvalidOperationException("Enumeration not started.");
                    if (_current >= _end) throw new InvalidOperationException("Enumeration ended.");
                    return _array[_current];
                }
            }

            object IEnumerator.Current => Current;

            public void Dispose()
            {
                
            }
        }

        public ListSlice(IReadOnlyList<T> array)
        {
           
            if (array == null)
                throw new ArgumentNullException(nameof(array));

            Array = array;
            Offset = 0;
            Count = array.Count;
        }
        
        public ListSlice(IReadOnlyList<T> array, int offset, int count)
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset), "Cannot be below zero.");
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Cannot be below zero.");
            
            Array = array;
            Offset = offset;
            Count = count;
        }

        public int Offset { get; }

        public IReadOnlyList<T> Array { get; }

        public IEnumerator<T> GetEnumerator()
        {
            return new SliceEnumerator(this);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void Add(T item)
        {
            throw new System.NotImplementedException();
        }

        public void Clear()
        {
            throw new System.NotImplementedException();
        }

        public bool Contains(T item)
        {
            throw new System.NotImplementedException();
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            var n = 0;
            foreach (var i in Array.Skip(Offset).Take(Count))
            {
                array[arrayIndex + n++] = i;
            }
        }

        public bool Remove(T item)
        {
            throw new System.NotImplementedException();
        }

        public int Count { get; }

        public bool IsReadOnly => true;
        public int IndexOf(T item)
        {
            throw new System.NotImplementedException();
        }

        public void Insert(int index, T item)
        {
            throw new System.NotImplementedException();
        }

        public void RemoveAt(int index)
        {
            throw new System.NotImplementedException();
        }

        public T this[int index]
        {
            get => Array[Offset + index];
            set => throw new System.NotImplementedException();
        }
    }
}
