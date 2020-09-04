//-----------------------------------------------------------------------
// <copyright file="ResizableMultiReaderRingBuffer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Annotations;
using Akka.Streams.Util;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    public class NothingToReadException : Exception
    {
        /// <summary>
        /// The singleton instance of this exception
        /// </summary>
        public static readonly NothingToReadException Instance = new NothingToReadException();

        private NothingToReadException()
        {
        }

#if SERIALIZATION
        /// <summary>
        /// Initializes a new instance of the <see cref="NothingToReadException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
        protected NothingToReadException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
    }

    /// <summary>
    /// TBD
    /// </summary>
    public interface ICursors
    {
        /// <summary>
        /// TBD
        /// </summary>
        IEnumerable<ICursor> Cursors { get; }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public interface ICursor
    {
        /// <summary>
        /// TBD
        /// </summary>
        long Cursor { get; set; }
    }

    internal interface IStreamBuffer<T>
    {
        bool IsEmpty { get; }

        long Length { get; }

        long AvailableData { get; }

        long CapacityLeft { get; }

        long Count(ICursor cursor);

        T Read(ICursor cursor);

        bool Write(T value);

        void InitCursor(ICursor cursor);

        void OnCursorRemoved(ICursor cursor);
    }

    public class DistinctRetainingMultiReaderBuffer<T> : RetainingMultiReaderBuffer<T>
    {
        public DistinctRetainingMultiReaderBuffer(long initialSize, long maxSize, ICursors cursors) : base(initialSize, maxSize, cursors)
        { }

        public override bool Write(T value)
        {
            return Buffer.Contains(value) || base.Write(value);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => $"DistinctRetainingMultiReaderBuffer(size={Length}, cursors={Cursors.Cursors.Count()})";
    }

    public class RetainingMultiReaderBuffer<T> : IStreamBuffer<T>
    {
        /// <summary>
        /// TBD
        /// </summary>
        protected readonly ICursors Cursors;

        protected T[] Buffer { get; private set; }

        /// <summary>
        /// The number of elements currently in the buffer.
        /// </summary>
        public long Length { get; private set; }

        public bool IsEmpty => Buffer.LongLength == 0;

        /// <summary>
        /// The maximum number of elements the buffer can still take.
        /// </summary>
        public long CapacityLeft => long.MaxValue - Length;

        // DO NOT REMOVE maxSize parameter, the parameters are fixed and passed through reflection
        public RetainingMultiReaderBuffer(long initialSize, long maxSize, ICursors cursors)
        {
            Cursors = cursors;

            if ((initialSize & (initialSize - 1)) != 0 || initialSize <= 0)
                throw new ArgumentException("initialSize must be a power of 2 that is > 0");

            // We don't care about the maximum size
            Buffer = new T[initialSize];
        }

        /// <summary>
        /// Returns the number of elements that the buffer currently contains for the given cursor.
        /// </summary>
        /// <param name="cursor">TBD</param>
        /// <returns>TBD</returns>
        public long Count(ICursor cursor) => Length - cursor.Cursor;

        public long AvailableData
        {
            get
            {
                var lowest = 0L;
                foreach (var cursor in Cursors.Cursors)
                    lowest = Math.Max(cursor.Cursor, lowest);

                return Length - lowest;
            }
        }

        public T Read(ICursor cursor)
        {
            var c = cursor.Cursor;
            if (c < Length)
            {
                cursor.Cursor++;
                return Buffer[c];
            }

            throw NothingToReadException.Instance;
        }

        public virtual bool Write(T value)
        {
            if (Length < Buffer.Length)
            {
                // if we have space left we can simply write and be done
                Buffer[Length] = value;
                Length++;
                return true;
            }
            
            if (Buffer.LongLength >= long.MaxValue) return false;

            // if we are full but can grow we do so
            // Array.Resize() does not work here, because it is limited to int.MaxValue
            var newLength = unchecked(Buffer.LongLength << 1);
            if (newLength < 0)
                newLength = long.MaxValue;
            var newArray = new T[newLength];

            Array.Copy(Buffer, newArray, Buffer.LongLength);
            Buffer = newArray;
            Buffer[Length] = value;
            Length++;
            return true;
        }

        public void InitCursor(ICursor cursor) => cursor.Cursor = 0;

        public void OnCursorRemoved(ICursor cursor)
        {
            // no op
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => $"RetainingMultiReaderBuffer(size={Length}, cursors={Cursors.Cursors.Count()})";
    }

    /// <summary>
    /// INTERNAL API
    /// A mutable RingBuffer that can grow in size and supports multiple readers.
    /// Contrary to many other ring buffer implementations this one does not automatically overwrite the oldest
    /// elements, rather, if full, the buffer tries to grow and rejects further writes if max capacity is reached.
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    [InternalApi]
    public class ResizableMultiReaderRingBuffer<T> : IStreamBuffer<T>
    {
        private readonly int _maxSizeBit;
        private T[] _array;
        
        /// <summary>
        /// Two counters counting the number of elements ever written and read; wrap-around is
        /// handled by always looking at differences or masked values
        /// </summary>
        private long _writeIndex;

        private long _readIndex; // the "oldest" of all read cursor indices, i.e. the one that is most behind

        /// <summary>
        /// Current array.length log2, we don't keep it as an extra field because <see cref="Int32Extensions.NumberOfTrailingZeros"/>
        /// is a JVM intrinsic compiling down to a `BSF` instruction on x86, which is very fast on modern CPUs
        /// </summary>
        private int LengthBit => BitOperations.TrailingZeroCount(_array.LongLength);

        // bit mask for converting a cursor into an array index
        private long Mask => long.MaxValue >> (63 - LengthBit);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="initialSize">TBD</param>
        /// <param name="maxSize">TBD</param>
        /// <param name="cursors">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        public ResizableMultiReaderRingBuffer(long initialSize, long maxSize, ICursors cursors)
        {
            Cursors = cursors;
            if ((initialSize & (initialSize - 1)) != 0 || initialSize <= 0 || initialSize > maxSize)
                throw new ArgumentException("initialSize must be a power of 2 that is > 0 and <= maxSize");

            
            if ((maxSize & (maxSize - 1)) != 0 || maxSize <= 0 || maxSize > int.MaxValue / 2)
                throw new ArgumentException("maxSize must be a power of 2 that is > 0 and < Int.MaxValue/2");

            _array = new T[initialSize];
            _maxSizeBit = BitOperations.TrailingZeroCount(maxSize);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected readonly ICursors Cursors;

        /// <summary>
        /// TBD
        /// </summary>
        protected T[] UnderlyingArray => _array;

        /// <summary>
        /// The number of elements currently in the buffer.
        /// </summary>
        public long Length => _writeIndex - _readIndex;

        public long AvailableData => Length;

        /// <summary>
        /// TBD
        /// </summary>
        public bool IsEmpty => Length == 0;

        /// <summary>
        /// TBD
        /// </summary>
        public bool NonEmpty => !IsEmpty;

        /// <summary>
        /// The number of elements the buffer can still take without having to be resized.
        /// </summary>
        public long ImmediatelyAvailable => _array.Length - Length;

        /// <summary>
        /// The maximum number of elements the buffer can still take.
        /// </summary>
        public long CapacityLeft => (1 << _maxSizeBit) - Length;

        /// <summary>
        /// Returns the number of elements that the buffer currently contains for the given cursor.
        /// </summary>
        /// <param name="cursor">TBD</param>
        /// <returns>TBD</returns>
        public long Count(ICursor cursor) => _writeIndex - cursor.Cursor;

        /// <summary>
        /// Initializes the given Cursor to the oldest buffer entry that is still available.
        /// </summary>
        /// <param name="cursor">TBD</param>
        public void InitCursor(ICursor cursor) => cursor.Cursor = _readIndex;

        /// <summary>
        /// Tries to write the given value into the buffer thereby potentially growing the backing array.
        /// Returns true if the write was successful and false if the buffer is full and cannot grow anymore.
        /// </summary> 
        /// <param name="value">TBD</param>
        /// <returns>TBD</returns>
        public bool Write(T value)
        {
            if (Length < _array.Length)
            {
                // if we have space left we can simply write and be done
                _array[_writeIndex & Mask] = value;
                _writeIndex++;
                return true;
            }
            if (LengthBit < _maxSizeBit)
            {
                // if we are full but can grow we do so
                // the growing logic is quite simple: we assemble all current buffer entries in the new array
                // in their natural order (removing potential wrap around) and rebase all indices to zero
                var r = _readIndex & Mask;

                var newLength = unchecked(_array.LongLength << 1);
                if (newLength < 0)
                    newLength = long.MaxValue;
                var newArray = new T[newLength];

                Array.Copy(_array, r, newArray, 0, _array.Length - r);
                Array.Copy(_array, 0, newArray, _array.Length - r, r);
                RebaseCursors(Cursors.Cursors);
                _array = newArray;
                var w = Length;
                _array[w & Mask] = value;
                _writeIndex = w + 1;
                _readIndex = 0;
                return true;
            }

            return false;
        }

        private void RebaseCursors(IEnumerable<ICursor> remaining)
        {
            foreach (var cursor in remaining)
                cursor.Cursor -= _readIndex;
        }

        /// <summary>
        /// Tries to read from the buffer using the given Cursor.
        /// If there are no more data to be read (i.e. the cursor is already
        /// at writeIx) the method throws <see cref="NothingToReadException"/>!
        /// </summary>
        /// <param name="cursor">TBD</param>
        /// <exception cref="NothingToReadException">TBD</exception>
        /// <returns>TBD</returns>
        public T Read(ICursor cursor)
        {
            var c = cursor.Cursor;
            if (c - _writeIndex < 0)
            {
                cursor.Cursor += 1;
                var ret = _array[c & Mask];
                if(c == _readIndex)
                    UpdateReadIndex();
                return ret;
            }

            throw NothingToReadException.Instance;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cursor">TBD</param>
        public void OnCursorRemoved(ICursor cursor)
        {
            if (cursor.Cursor == _readIndex) // if this cursor is the last one it must be at readIx
                UpdateReadIndex();
        }

        private void UpdateReadIndex()
        {
            var newReadIx = _writeIndex + MinCursor(Cursors.Cursors, 0);
            while (_readIndex != newReadIx)
            {
                _array[_readIndex & Mask] = default;
                _readIndex++;
            }
        }

        private long MinCursor(IEnumerable<ICursor> remaining, long result)
        {
            foreach (var cursor in remaining)
                result = Math.Min(cursor.Cursor - _writeIndex, result);

            return result;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => $"ResizableMultiReaderRingBuffer(size={Length}, writeIx={_writeIndex}, readIx={_readIndex}, cursors={Cursors.Cursors.Count()})";
    }
}
