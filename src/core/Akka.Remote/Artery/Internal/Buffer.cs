using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace Akka.Remote.Artery.Internal
{
    public class UnsupportedOperationException : Exception
    {
        public UnsupportedOperationException(string message) : base (message) { }
        public UnsupportedOperationException(string message, Exception innerException) : base(message, innerException) { }
        public UnsupportedOperationException() { }
    }

    internal abstract class Buffer<T>
    {
        public class InvalidMarkException : Exception
        {
            public InvalidMarkException(string message) : base(message) { }
            public InvalidMarkException(string message, Exception innerException) : base(message, innerException) { }
            public InvalidMarkException() { }
        }

        public class OverflowException : Exception
        {
            public OverflowException(string message) : base(message) { }
            public OverflowException(string message, Exception innerException) : base(message, innerException) { }
            public OverflowException() { }
        }

        public class UnderflowException : Exception
        {
            public UnderflowException(string message) : base(message) { }
            public UnderflowException(string message, Exception innerException) : base(message, innerException) { }
            public UnderflowException() { }
        }

        public class ReadOnlyException : Exception
        {
            public ReadOnlyException(string message) : base(message) { }
            public ReadOnlyException(string message, Exception innerException) : base(message, innerException) { }
            public ReadOnlyException() { }
        }

        private int _mark = -1;
        private int _position = 0;
        private int _limit;
        private int _capacity;

        /// <summary>
        /// Returns this buffer's capacity.
        /// </summary>
        public virtual int Capacity => _capacity;
        /// <summary>
        /// Returns the number of elements between the current position and the limit.
        /// </summary>
        public int Remaining => _limit - _position;
        /// <summary>
        /// Tells whether there are any elements between the current position and the limit.
        /// </summary>
        public bool HasRemaining => _position < _limit;
        /// <summary>
        /// Tells whether or not this buffer is read-only.
        /// </summary>
        public abstract bool IsReadOnly { get; }
        /// <summary>
        /// Tells whether or not this buffer is backed by an accessible array.
        ///
        /// <p> If this method returns {@code true} then the {@link #array() array}
        /// and {@link #arrayOffset() arrayOffset} methods may safely be invoked.</p>
        /// </summary>
        /// <returns>
        /// {@code true} if, and only if, this buffer is backed by an array and is not read-only
        /// </returns>
        public abstract bool HasArray { get; }
        /// <summary>
        /// Returns the array that backs this buffer  <i>(optional operation)</i>.
        ///
        /// <p> This method is intended to allow array-backed buffers to be
        /// passed to native code more efficiently. Concrete subclasses
        /// provide more strongly-typed return values for this method.</p>
        ///
        /// <p> Modifications to this buffer's content will cause the returned
        /// array's content to be modified, and vice versa.</p>
        ///
        /// <p> Invoke the {@link #hasArray hasArray} method before invoking this
        /// method in order to ensure that this buffer has an accessible backing
        /// array.  </p>
        /// </summary>
        public abstract Memory<T> Array { get; }
        /// <summary>
        /// Returns the offset within this buffer's backing array of the first
        /// element of the buffer  <i>(optional operation)</i>.
        ///
        /// <p> If this buffer is backed by an array then buffer position <i>p</i>
        /// corresponds to array index <i>p</i> + {@code arrayOffset()}.</p>
        ///
        /// <p> Invoke the {@link #hasArray hasArray} method before invoking this
        /// method in order to ensure that this buffer has an accessible backing
        /// array.  </p>
        /// </summary>
        public abstract int ArrayOffset { get; }

        /// <summary>
        /// Creates a new buffer with the given mark, position, limit, and capacity,
        /// after checking invariants.
        /// </summary>
        /// <param name="mark"></param>
        /// <param name="pos"></param>
        /// <param name="lim"></param>
        /// <param name="cap"></param>
        protected Buffer(int mark, int pos, int lim, int cap)
        {
            if (cap < 0)
                throw new IllegalArgumentException($"Capacity < 0: ({cap} < 0)");
            _capacity = cap;
            Limit(lim);
            Position(pos);
            if (mark >= 0)
            {
                if(mark > pos)
                    throw new IllegalArgumentException($"mark > position: ({mark} > {pos})");
                _mark = mark;
            }
        }

        /// <summary>
        /// Returns this buffer's position.
        /// </summary>
        /// <returns></returns>
        public int Position() => _position;
        /// <summary>
        /// Sets this buffer's position.  If the mark is defined and larger than the
        /// new position then it is discarded.
        /// </summary>
        /// <param name="value">The new position value; must be non-negative and no larger than the current limit</param>
        /// <returns>This buffer</returns>
        public Buffer<T> Position(int value)
        {
            if (value > _limit)
                throw new IllegalArgumentException($"New position > Limit: ({value} > {_limit})");
            if (value < 0)
                throw new IllegalArgumentException($"New position < 0: ({value} < 0)");
            _position = value;
            if (_mark > _position) _mark = -1;
            return this;
        }

        /// <summary>
        /// Returns this buffer's limit.
        /// </summary>
        /// <returns></returns>
        public int Limit() => _limit;
        /// <summary>
        /// Sets this buffer's limit.  If the position is larger than the new limit
        /// then it is set to the new limit.  If the mark is defined and larger than
        /// the new limit then it is discarded.
        /// </summary>
        /// <param name="value"></param>
        /// <returns>This buffer</returns>
        public Buffer<T> Limit(int value)
        {
            if (value > _capacity)
                throw new IllegalArgumentException($"New limit > Capacity: ({value} > {_capacity}");
            if (value < 0)
                throw new IllegalArgumentException($"New limit < 0: ({value} < 0)");
            _limit = value;
            if (_position > _limit) _position = _limit;
            if (_mark > _limit) _mark = -1;
            return this;
        }

        /// <summary>
        /// Sets this buffer's mark at its position.
        /// </summary>
        /// <returns>This buffer</returns>
        public Buffer<T> Mark()
        {
            _mark = _position;
            return this;
        }

        /// <summary>
        /// Resets this buffer's position to the previously-marked position.
        /// <p> Invoking this method neither changes nor discards the mark's value. </p>
        /// </summary>
        /// <returns>This buffer</returns>
        public Buffer<T> Reset()
        {
            if(_mark < 0)
                throw new InvalidMarkException();
            _position = _mark;
            return this;
        }

        /// <summary>
        /// Clears this buffer.  The position is set to zero, the limit is set to
        /// the capacity, and the mark is discarded.
        ///
        /// <p> Invoke this method before using a sequence of channel-read or
        /// <i>put</i> operations to fill this buffer.  For example:</p>
        /// <blockquote><pre>
        /// buf.clear();     // Prepare buffer for reading
        /// in.read(buf);    // Read data</pre></blockquote>
        ///
        /// <p> This method does not actually erase the data in the buffer, but it
        /// is named as if it did because it will most often be used in situations
        /// in which that might as well be the case. </p>
        /// </summary>
        /// <returns>This buffer</returns>
        public Buffer<T> Clear()
        {
            _position = 0;
            _limit = _capacity;
            _mark = -1;
            return this;
        }

        /// <summary>
        /// Flips this buffer.  The limit is set to the current position and then
        /// the position is set to zero.  If the mark is defined then it is
        /// discarded.
        ///
        /// <p> After a sequence of channel-read or <i>put</i> operations, invoke
        /// this method to prepare for a sequence of channel-write or relative
        /// <i>get</i> operations.  For example:</p>
        ///
        /// <blockquote><pre>
        /// buf.put(magic);    // Prepend header
        /// in.read(buf);      // Read data into rest of buffer
        /// buf.flip();        // Flip buffer
        /// out.write(buf);    // Write header + data to channel</pre></blockquote>
        ///
        /// <p> This method is often used in conjunction with the {@link
        /// java.nio.ByteBuffer#compact compact} method when transferring data from
        /// one place to another.  </p>
        /// </summary>
        /// <returns>This buffer</returns>
        public Buffer<T> Flip()
        {
            _limit = _position;
            _position = 0;
            _mark = -1;
            return this;
        }

        /// <summary>
        /// Rewinds this buffer.  The position is set to zero and the mark is
        /// discarded.
        ///
        /// <p> Invoke this method before a sequence of channel-write or <i>get</i>
        /// operations, assuming that the limit has already been set
        /// appropriately.  For example:</p>
        ///
        /// <blockquote><pre>
        /// out.write(buf);    // Write remaining data
        /// buf.rewind();      // Rewind buffer
        /// buf.get(array);    // Copy data into array</pre></blockquote>
        /// </summary>
        /// <returns>This buffer</returns>
        public Buffer<T> Rewind()
        {
            _position = 0;
            _mark = -1;
            return this;
        }

        /// <summary>
        /// Creates a new buffer whose content is a shared subsequence of
        /// this buffer's content.
        ///
        /// <p> The content of the new buffer will start at this buffer's current
        /// position.  Changes to this buffer's content will be visible in the new
        /// buffer, and vice versa; the two buffers' position, limit, and mark
        /// values will be independent.</p>
        ///
        /// <p> The new buffer's position will be zero, its capacity and its limit
        /// will be the number of elements remaining in this buffer, its mark will be
        /// undefined. </p>
        /// 
        /// </summary>
        /// <returns>The new buffer</returns>
        public abstract Buffer<T> Slice();

        /// <summary>
        /// Creates a new buffer that shares this buffer's content.
        ///
        /// <p> The content of the new buffer will be that of this buffer.  Changes
        /// to this buffer's content will be visible in the new buffer, and vice
        /// versa; the two buffers' position, limit, and mark values will be
        /// independent.</p>
        ///
        /// <p> The new buffer's capacity, limit, position and mark values will be
        /// identical to those of this buffer.</p>
        /// </summary>
        /// <returns>The new buffer</returns>
        public abstract Buffer<T> Duplicate();

        /// <summary>
        /// Checks the current position against the limit, throwing a {@link
        /// BufferUnderflowException} if it is not smaller than the limit, and then
        /// increments the position.
        /// </summary>
        /// <returns></returns>
        protected int NextGetIndex()
            => _position < _limit ? _position++ : throw new UnderflowException();

        protected int NextGetIndex(int nb)
        {
            if(_limit - _position < nb)
                throw new UnderflowException();
            var p = _position;
            _position += nb;
            return p;
        }

        /// <summary>
        /// Checks the current position against the limit, throwing a {@link
        /// BufferOverflowException} if it is not smaller than the limit, and then
        /// increments the position.
        /// </summary>
        /// <returns></returns>
        protected int NextPutIndex()
            => _position < _limit ? _position++ : throw new OverflowException();

        protected int NextPutIndex(int nb)
        {
            if (_limit - _position < nb)
                throw new OverflowException();
            var p = _position;
            _position += nb;
            return p;
        }

        /// <summary>
        /// Checks the given index against the limit, throwing an {@link
        /// IndexOutOfBoundsException} if it is not smaller than the limit
        /// or is smaller than zero.
        /// </summary>
        /// <param name="i"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected int CheckIndex(int i)
        {
            if ((i < 0) || (i >= _limit))
                throw new IndexOutOfRangeException();
            return i;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected int CheckIndex(int i, int nb)
        {
            if ((i < 0) || (nb > _limit - i))
                throw new IndexOutOfRangeException();
            return i;
        }

        protected int MarkValue => _mark;

        protected void Truncate()
        {
            _mark = -1;
            _position = 0;
            _limit = 0;
            _capacity = 0;
        }

        protected void DiscardMark() => _mark = -1;

        protected static void CheckBounds(int off, int len, int size)
        {
            if ((off | len | (off + len) | (size - (off + len))) < 0)
                throw new IndexOutOfRangeException();
        }
    }
}
