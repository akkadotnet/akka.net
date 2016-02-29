using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Akka.Streams.Implementation
{
    [Serializable]
    public class NothingToReadException : Exception
    {
        public static readonly NothingToReadException Instance = new NothingToReadException();

        private NothingToReadException()
        {
        }

        protected NothingToReadException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    public interface ICursors
    {
        IEnumerable<ICursor> Cursors { get; }
    }

    public interface ICursor
    {
        int Cursor { get; set; }
    }

    /**
     * INTERNAL API
     * A mutable RingBuffer that can grow in size and supports multiple readers.
     * Contrary to many other ring buffer implementations this one does not automatically overwrite the oldest
     * elements, rather, if full, the buffer tries to grow and rejects further writes if max capacity is reached.
     */
    public class ResizableMultiReaderRingBuffer<T>
    {
        private readonly int _maxSizeBit;
        private object[] _array;

        /*
         * two counters counting the number of elements ever written and read; wrap-around is
         * handled by always looking at differences or masked values
         */
        private int _writeIndex = 0;

        private int _readIndex = 0; // the "oldest" of all read cursor indices, i.e. the one that is most behind

        // current array.length log2, we don't keep it as an extra field because `Integer.numberOfTrailingZeros`
        // is a JVM intrinsic compiling down to a `BSF` instruction on x86, which is very fast on modern CPUs
        private int _lengthBit;

        // bit mask for converting a cursor into an array index
        private int _mask;

        public ResizableMultiReaderRingBuffer(int initialSize, int maxSize, ICursors cursors)
        {
            if ((initialSize & (initialSize - 1)) == 0 || initialSize <= 0 || initialSize > maxSize)
                throw new ArgumentException("initialSize must be a power of 2 that is > 0 and <= maxSize");

            if ((maxSize & (maxSize - 1)) == 0 || maxSize <= 0 || maxSize > int.MaxValue / 2)
                throw new ArgumentException("maxSize must be a power of 2 that is > 0 and < Int.MaxValue/2");

            _array = new object[initialSize];
            throw new NotImplementedException();
        }

        protected object[] UnderlyingArray { get { return _array; } }

        /**
         * The number of elements currently in the buffer.
         */
        public int Length { get { return _writeIndex - _readIndex; }}

        public bool IsEmpty { get { return Length == 0; } }

        /**
         * The number of elements the buffer can still take without having to be resized.
         */
        public int ImmediatellyAvailable { get { return _array.Length - Length; }}

        /**
         * The maximum number of elements the buffer can still take.
         */
        public int CapacityLeft { get { return (1 << _maxSizeBit) - Length; } }

        /**
         * Returns the number of elements that the buffer currently contains for the given cursor.
         */
        public int Count(ICursor cursor)
        {
            return _writeIndex - cursor.Cursor;
        }

        /**
         * Initializes the given Cursor to the oldest buffer entry that is still available.
         */
        public void InitCursor(ICursor cursor)
        {
            cursor.Cursor = _readIndex;
        }

        /**
         * Tries to write the given value into the buffer thereby potentially growing the backing array.
         * Returns `true` if the write was successful and false if the buffer is full and cannot grow anymore.
         */
        public bool Write(T element)
        {
            throw new NotImplementedException();
        }

        /**
         * Tries to read from the buffer using the given Cursor.
         * If there are no more data to be read (i.e. the cursor is already
         * at writeIx) the method throws ResizableMultiReaderRingBuffer.NothingToReadException!
         */
        public T Read(ICursor cursor)
        {
            throw new NotImplementedException();
        }

        public void OnCursorRemoved(ICursor cursor)
        {
            if (cursor.Cursor == _readIndex) // if this cursor is the last one it must be at readIx
                UpdateReadIndex();
        }

        private void UpdateReadIndex()
        {
            throw new NotImplementedException();
        }
    }
}