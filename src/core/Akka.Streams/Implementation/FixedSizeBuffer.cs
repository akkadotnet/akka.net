using System;

namespace Akka.Streams.Implementation
{
    public static class FixedSizeBuffer
    {
        /**
         * INTERNAL API
         *
         * Returns a fixed size buffer backed by an array. The buffer implementation DOES NOT check agains overflow or
         * underflow, it is the responsibility of the user to track or check the capacity of the buffer before enqueueing
         * dequeueing or dropping.
         *
         * Returns a specialized instance for power-of-two sized buffers.
         */
        public static FixedSizeBuffer<T> Create<T>(int size)
        {
            if (size < 1) throw new ArgumentException("buffer size must be positive");
            else if (((size - 1) & size) == 0) return new PowerOfTwoFixedSizeBuffer<T>(size);
            else return new ModuloFixedSizeBuffer<T>(size);
        }
    }

    public abstract class FixedSizeBuffer<T>
    {
        protected readonly int Size;
        protected int ReadIndex = 0;
        protected int WriteIndex = 0;

        private readonly T[] _buffer;

        protected FixedSizeBuffer(int size)
        {
            Size = size;
            _buffer = new T[size];
        }

        public int Used { get { return WriteIndex - ReadIndex; } }
        public bool IsFull { get { return Used == Size; } }
        public bool IsEmpty { get { return Used == 0; } }

        protected abstract int ToOffset(int index);

        public int Enqueue(T element)
        {
            Put(WriteIndex, element);
            var ret = WriteIndex;
            WriteIndex++;
            return ret;
        }

        public void Put(int index, T element)
        {
            _buffer[ToOffset(index)] = element;
        }

        public T Get(int index)
        {
            return _buffer[ToOffset(index)];
        }

        public T Peek()
        {
            return Get(ReadIndex);
        }

        public T Dequeue()
        {
            var result = Get(ReadIndex);
            DropHead();
            return result;
        }

        public void Clear()
        {
            _buffer.Initialize();
            ReadIndex = 0;
            WriteIndex = 0;
        }

        public void DropHead()
        {
            Put(ReadIndex, default(T));
            ReadIndex++;
        }

        public void DropTail()
        {
            WriteIndex--;
            Put(WriteIndex, default(T));
        }
    }

    public sealed class ModuloFixedSizeBuffer<T> : FixedSizeBuffer<T>
    {
        public ModuloFixedSizeBuffer(int size) : base(size)
        {
        }

        protected override int ToOffset(int index)
        {
            return index%Size;
        }
    }

    public sealed class PowerOfTwoFixedSizeBuffer<T> : FixedSizeBuffer<T>
    {
        private readonly int _mask;
        public PowerOfTwoFixedSizeBuffer(int size) : base(size)
        {
            _mask = size - 1;
        }

        protected override int ToOffset(int index)
        {
            return index & _mask;
        }
    }
}