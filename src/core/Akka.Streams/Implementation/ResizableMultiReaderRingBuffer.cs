using System;
using System.Collections.Generic;
using System.Linq;
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
        private int LengthBit => NumberOfTrailingZeros(_array.Length);

        // bit mask for converting a cursor into an array index
        private int Mask => Int32.MaxValue >> (31 - LengthBit);

        public ResizableMultiReaderRingBuffer(int initialSize, int maxSize, ICursors cursors)
        {
            Cursors = cursors;
            if ((initialSize & (initialSize - 1)) != 0 || initialSize <= 0 || initialSize > maxSize)
                throw new ArgumentException("initialSize must be a power of 2 that is > 0 and <= maxSize");

            
            if ((maxSize & (maxSize - 1)) != 0 || maxSize <= 0 || maxSize > int.MaxValue / 2)
                throw new ArgumentException("maxSize must be a power of 2 that is > 0 and < Int.MaxValue/2");

            _array = new object[initialSize];
            _maxSizeBit = NumberOfTrailingZeros(maxSize);
        }

        protected readonly ICursors Cursors;

        protected object[] UnderlyingArray => _array;

        /**
         * The number of elements currently in the buffer.
         */
        public int Length => _writeIndex - _readIndex;

        public bool IsEmpty => Length == 0;

        public bool NonEmpty => !IsEmpty;

        /**
         * The number of elements the buffer can still take without having to be resized.
         */
        public int ImmediatellyAvailable => _array.Length - Length;

        /**
         * The maximum number of elements the buffer can still take.
         */
        public int CapacityLeft => (1 << _maxSizeBit) - Length;

        /**
         * Returns the number of elements that the buffer currently contains for the given cursor.
         */
        public int Count(ICursor cursor) => _writeIndex - cursor.Cursor;

        /**
         * Initializes the given Cursor to the oldest buffer entry that is still available.
         */
        public void InitCursor(ICursor cursor) => cursor.Cursor = _readIndex;

        /**
         * Tries to write the given value into the buffer thereby potentially growing the backing array.
         * Returns `true` if the write was successful and false if the buffer is full and cannot grow anymore.
         */
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
                var newArray = new object[_array.Length << 1];
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

        /**
         * Tries to read from the buffer using the given Cursor.
         * If there are no more data to be read (i.e. the cursor is already
         * at writeIx) the method throws ResizableMultiReaderRingBuffer.NothingToReadException!
         */
        public T Read(ICursor cursor)
        {
            var c = cursor.Cursor;
            if (c - _writeIndex < 0)
            {
                cursor.Cursor += 1;
                var ret = (T)_array[c & Mask];
                if(c == _readIndex)
                    UpdateReadIndex();
                return ret;
            }

            throw NothingToReadException.Instance;
        }

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
                _array[_readIndex & Mask] = null;
                _readIndex++;
            }
        }

        private int MinCursor(IEnumerable<ICursor> remaining, int result)
        {
            foreach (var cursor in remaining)
                result = Math.Min(cursor.Cursor - _writeIndex, result);

            return result;
        }

        public override string ToString() => $"ResizableMultiReaderRingBuffer(size={Length}, writeIx={_writeIndex}, readIx={_readIndex}, cursors={Cursors.Cursors.Count()})";

        private static int NumberOfTrailingZeros(int i)
        {
            if (i == 0)
                return 32;

            var x = (i & -i) - 1;
            x -= ((x >> 1) & 0x55555555);
            x = (((x >> 2) & 0x33333333) + (x & 0x33333333));
            x = (((x >> 4) + x) & 0x0f0f0f0f);
            x += (x >> 8);
            x += (x >> 16);
            return (x & 0x0000003f);
        }
    }
}