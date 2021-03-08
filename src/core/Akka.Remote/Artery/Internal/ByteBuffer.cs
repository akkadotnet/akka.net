using System;
using System.Buffers;
using System.Buffers.Binary;
using Akka.IO;

#nullable enable
namespace Akka.Remote.Artery.Internal
{
    internal sealed class ByteBuffer : 
        Buffer<byte>, 
        IEquatable<ByteBuffer>, 
        IComparable<ByteBuffer>,
        IDisposable
    {
        private readonly IMemoryOwner<byte>? _memoryOwner;
        private readonly Memory<byte> _backingBuffer;
        private readonly Memory<byte> _buffer;
        private readonly int _offset;

        private bool _bigEndian;
        private bool _nativeByteOrder;

        /// <summary>
        /// Allocates a new byte buffer using a <see cref="Memory{T}"/> as its backing.
        ///
        /// <para>
        /// The new buffer's position will be zero, its limit will be its
        /// capacity, its mark will be undefined, each of its elements will be
        /// initialized to zero, and its byte order will be
        /// {@link ByteOrder#BIG_ENDIAN BIG_ENDIAN}.
        /// </para>
        /// </summary>
        /// <param name="capacity">The new buffer's capacity, in bytes</param>
        /// <returns>The new byte buffer</returns>
        public static ByteBuffer Allocate(int capacity) => new ByteBuffer(capacity);

        /// <summary>
        /// Wraps a byte array into a <see cref="Memory{T}"/> buffer.
        ///
        /// <para>
        /// The new buffer will be backed by the given byte array;
        /// that is, modifications to the buffer will cause the array to be modified
        /// and vice versa.  The new buffer's capacity will be
        /// <paramref name="array.Length"/>, its position will be <paramref name="offset"/>, its limit
        /// will be  <paramref name="offset"/> + <paramref name="length"/>, its mark will be undefined, and its
        /// byte order will be big endian.
        /// Its backing array will be the given array, and
        /// its array offset will be zero.
        /// </para>
        /// </summary>
        /// <param name="array">
        ///         The array that will back the new buffer
        /// </param>
        /// <param name="offset">
        ///         The offset of the sub-array to be used; must be non-negative and
        ///         no larger than <paramref name="array.Length"/>.  The new buffer's position
        ///         will be set to this value.
        /// </param>
        /// <param name="length">
        ///         The length of the subarray to be used;
        ///         must be non-negative and no larger than
        ///         {@code array.length - offset}.
        ///         The new buffer's limit will be set to {@code offset + length}.
        /// </param>
        /// <returns>The new byte buffer</returns>
        public static ByteBuffer Wrap(byte[] array, int offset, int length) => new ByteBuffer(array, offset, length);

        /// <summary>
        /// Wraps a byte array into a <see cref="Memory{T}"/> buffer.
        /// <para>
        /// The new buffer will be backed by the given byte array;
        /// that is, modifications to the buffer will cause the array to be modified
        /// and vice versa.  The new buffer's capacity and limit will be
        /// <see cref="Array.Length"/>, its position will be zero, its mark will be
        /// undefined, and its byte order will be big endian.
        /// Its backing array will be the given array, and its
        /// array offset will be zero.
        /// </para>
        /// </summary>
        /// <param name="array">The array that will back this buffer</param>
        /// <returns>The new byte buffer</returns>
        public static ByteBuffer Wrap(byte[] array) => new ByteBuffer(array, 0, array.Length);

        private ByteBuffer(byte[] array, int offset, int length) : base(-1, 0, length, length)
        {
            _memoryOwner = null;
            _buffer = _backingBuffer = new Memory<byte>(array, offset, length);
            _offset = 0;

            _bigEndian = true;
            _nativeByteOrder = !BitConverter.IsLittleEndian;
        }

        private ByteBuffer(int capacity) : base(-1, 0, capacity, capacity)
        {
            _memoryOwner = MemoryPool<byte>.Shared.Rent(capacity);
            _buffer = _backingBuffer = _memoryOwner.Memory;
            _offset = 0;

            _bigEndian = true;
            _nativeByteOrder = !BitConverter.IsLittleEndian;
        }

        private ByteBuffer(int mark, int pos, int lim, int cap, Memory<byte> hb, int offset)
            : base(mark, pos, lim, cap)
        {
            _memoryOwner = null;
            _backingBuffer = hb;
            _offset = offset;
            _buffer = _backingBuffer.Slice(_offset);

            _bigEndian = true;
            _nativeByteOrder = !BitConverter.IsLittleEndian;
        }

        private ByteBuffer(Memory<byte> hb, int offset, int capacity)
            : base (-1, 0, capacity, capacity)
        {
            _memoryOwner = null;
            _backingBuffer = hb;
            _offset = offset;
            _buffer = hb.Slice(offset, capacity);

            _bigEndian = true;
            _nativeByteOrder = !BitConverter.IsLittleEndian;
        }

        public ByteOrder Order() => _bigEndian ? ByteOrder.BigEndian : ByteOrder.LittleEndian;

        public ByteBuffer Order(ByteOrder bo)
        {
            _bigEndian = bo == ByteOrder.BigEndian;
            _nativeByteOrder = _bigEndian == !BitConverter.IsLittleEndian;
            return this;
        }

        public int AlignmentOffset(int index, int unitSize)
        {
            if(index < 0)
                throw new ArgumentException($"Index less than zero: {index}");
            if(unitSize < 1 || (unitSize & (unitSize - 1)) != 0)
                throw new ArgumentException($"Unit size not a power of two: {unitSize}");
            return index % unitSize;
        }

        public ByteBuffer AlignedSlice(int unitSize)
        {
            var pos = Position();
            var lim = Limit();

            var posMod = AlignmentOffset(pos, unitSize);
            var limMod = AlignmentOffset(lim, unitSize);

            // Round up the position to align with unit size
            var alignedPos = posMod > 0 ? pos + (unitSize - posMod) : pos;
            // Round down the limit to align with unit size
            var alignedLim = lim - limMod;

            if (alignedPos > lim || alignedLim < pos)
                alignedPos = alignedLim = pos;

            return Slice(alignedPos, alignedLim);
        }

        public ByteBuffer Slice(int pos, int lim)
            => new ByteBuffer(_backingBuffer, pos, lim - pos);

        #region concrete method implementations

        public override bool IsReadOnly => false;

        /// <inheritdoc cref="Buffer{T}.HasArray"/>
        public override bool HasArray => !_backingBuffer.IsEmpty && !IsReadOnly;

        /// <inheritdoc cref="Buffer{T}.Array"/>
        public override Memory<byte> Array
            => _backingBuffer.IsEmpty ? throw new UnsupportedOperationException()
                : IsReadOnly ? throw new ReadOnlyException() : _backingBuffer;

        /// <inheritdoc cref="Buffer{T}.ArrayOffset"/>
        public override int ArrayOffset
            => _backingBuffer.IsEmpty ? throw new UnsupportedOperationException()
                : IsReadOnly ? throw new ReadOnlyException() : _offset;

        #region Covariant return type overrides

        /// <inheritdoc cref="Buffer{T}.Position(int)"/>
        public new ByteBuffer Position(int value)
        {
            base.Position(value);
            return this;
        }

        /// <inheritdoc cref="Buffer{T}.Limit(int)"/>
        public new ByteBuffer Limit(int value)
        {
            base.Limit(value);
            return this;
        }

        /// <inheritdoc cref="Buffer{T}.Mark"/>
        public new ByteBuffer Mark()
        {
            base.Mark();
            return this;
        }

        /// <inheritdoc cref="Buffer{T}.Reset"/>
        public new ByteBuffer Reset()
        {
            base.Reset();
            return this;
        }

        /// <inheritdoc cref="Buffer{T}.Clear"/>
        public new ByteBuffer Clear()
        {
            base.Clear();
            return this;
        }

        /// <inheritdoc cref="Buffer{T}.Flip"/>
        public new ByteBuffer Flip()
        {
            base.Flip();
            return this;
        }

        /// <inheritdoc cref="Buffer{T}.Rewind"/>
        public new ByteBuffer Rewind()
        {
            base.Rewind();
            return this;
        }

        /// <inheritdoc cref="Buffer{T}.Slice"/>
        public override Buffer<byte> Slice()
            => new ByteBuffer(_backingBuffer, _offset + Position(), Remaining);

        /// <inheritdoc cref="Buffer{T}.Duplicate"/>
        public override Buffer<byte> Duplicate()
            => new ByteBuffer(MarkValue, Position(), Limit(), Capacity, _backingBuffer, _offset);

        #endregion

        #endregion

        /// <summary>
        /// Compacts this buffer  <i>(optional operation)</i>.
        ///
        /// <p> The bytes between the buffer's current position and its limit,
        /// if any, are copied to the beginning of the buffer.  That is, the
        /// byte at index <i>p</i> = {@code position()} is copied
        /// to index zero, the byte at index <i>p</i> + 1 is copied
        /// to index one, and so forth until the byte at index
        /// {@code limit()} - 1 is copied to index
        /// <i>n</i> = {@code limit()} - {@code 1} - <i>p</i>.
        /// The buffer's position is then set to <i>n+1</i> and its limit is set to
        /// its capacity.  The mark, if defined, is discarded.</p>
        ///
        /// <p> The buffer's position is set to the number of bytes copied,
        /// rather than to zero, so that an invocation of this method can be
        /// followed immediately by an invocation of another relative <i>put</i>
        /// method. </p>
        ///
        /// <p> Invoke this method after writing data from a buffer in case the
        /// write was incomplete.  The following loop, for example, copies bytes
        /// from one channel to another via the buffer {@code buf}:</p>
        ///
        /// <blockquote><pre><code>
        /// <![CDATA[
        ///   buf.clear();          // Prepare buffer for use
        ///   while (in.read(buf) >= 0 || buf.position != 0) {
        ///       buf.flip();
        ///       out.write(buf);
        ///       buf.compact();    // In case of partial write
        ///   }
        /// ]]>
        /// </code></pre></blockquote>
        /// 
        /// </summary>
        /// <returns>This buffer</returns>
        /// <exception cref="Buffer{T}.ReadOnlyException">If this buffer is read-only</exception>
        public ByteBuffer Compact()
        {
            var length = Remaining;
            var span = _buffer.Span.Slice(Position(), length);
            span.CopyTo(_buffer.Span);
            Clear().Position(length);
            return this;
        }

        public override string ToString()
            => $"{GetType().Name}[pos={Position()} lim={Limit()} cap={Capacity}]";

        public override int GetHashCode()
        {
            unchecked
            {
                var h = 1;
                var p = Position();
                for (var i = Limit() - 1; i >= p; i--)
                    h = 31 * h + Get(i);

                return h;
            }
        }

        public bool Equals(ByteBuffer other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            if (Remaining != other.Remaining) return false;

            var otherSpan = other._buffer.Span.Slice(other.Position(), Remaining);
            return otherSpan.SequenceEqual(_buffer.Span.Slice(Position(), Remaining));
        }

        public override bool Equals(object obj)
        {
            if (obj is null) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is ByteBuffer other && Equals(other);
        }

        public int CompareTo(ByteBuffer other)
        {
            var i = Mismatch(other);
            return i >= 0 ? Get(i).CompareTo(other.Get(i)) : Remaining - other.Remaining;
        }

        public int Mismatch(ByteBuffer other)
        {
            var length = Math.Min(Remaining, other.Remaining);
            var aSpan = _buffer.Span.Slice(Position(), length);
            var bSpan = other._buffer.Span.Slice(other.Position(), length);
            var i = 0;
            for (; i < length; i++)
            {
                if (aSpan[i] != bSpan[i])
                    break;
            }

            return (i == -1 && Remaining != other.Remaining) ? length : i;
        }

        #region Put ops
        /// <summary>
        /// Relative bulk <i>put</i> method <i>(optional operation)</i>.
        ///
        /// <p> This method transfers the bytes remaining in the given source
        /// buffer into this buffer.  If there are more bytes remaining in the
        /// source buffer than in this buffer, that is, if</p>
        /// <code>src.remaining() &gt; remaining()</code>,
        /// hen no bytes are transferred and a <see cref="Buffer{T}.OverflowException"/> is thrown.
        ///
        /// <p> Otherwise, this method copies
        /// <i>n</i> = {@code src.remaining()} bytes from the given
        /// buffer into this buffer, starting at each buffer's current position.
        /// The positions of both buffers are then incremented by <i>n</i>.</p>
        ///
        /// <p> In other words, an invocation of this method of the form
        /// {@code dst.put(src)} has exactly the same effect as the loop</p>
        ///
        /// <code><![CDATA[
        ///     while (src.hasRemaining())
        ///         dst.put(src.get());
        /// ]]></code>
        ///
        /// except that it first checks that there is sufficient space in this
        /// buffer and it is potentially much more efficient.
        /// </summary>
        /// <param name="src">
        /// The source buffer from which bytes are to be read; must not be this buffer
        /// </param>
        /// <returns>This buffer</returns>
        /// <exception cref="Buffer{T}.OverflowException">
        /// If there is insufficient space in this buffer for the remaining bytes in the source buffer
        /// </exception>
        /// <exception cref="ArgumentException">If the source buffer is this buffer</exception>
        /// <exception cref="Buffer{T}.ReadOnlyException">If this buffer is read-only</exception>
        public ByteBuffer Put(ByteBuffer src)
        {
            if(src.Equals(this))
                throw new ArgumentException("The source buffer is this buffer");
            if(IsReadOnly)
                throw new ReadOnlyException();
            var n = src.Remaining;
            if(n > Remaining)
                throw new UnderflowException();
            
            src._buffer.Span.Slice(src.NextGetIndex(n), n)
                .CopyTo(_buffer.Span.Slice(NextPutIndex(n)));
            return this;
        }

        /// <summary>
        /// Relative bulk <i>put</i> method  <i>(optional operation)</i>.
        ///
        /// <p> This method transfers bytes into this buffer from the given
        /// source array.  If there are more bytes to be copied from the array
        /// than remain in this buffer, that is, if
        /// {@code length} {@code >} {@code remaining()}, then no
        /// bytes are transferred and a {@link BufferOverflowException} is
        /// thrown.</p>
        ///
        /// <p> Otherwise, this method copies {@code length} bytes from the
        /// given array into this buffer, starting at the given offset in the array
        /// and at the current position of this buffer.  The position of this buffer
        /// is then incremented by {@code length}.</p>
        ///
        /// <p> In other words, an invocation of this method of the form
        /// <code>dst.put(src, off, len)</code> has exactly the same effect as
        /// the loop</p>
        ///
        /// <code>
        /// <![CDATA[
        ///     for (int i = off; i < off + len; i++)
        ///         dst.put(a[i]);
        /// ]]>
        ///
        /// except that it first checks that there is sufficient space in this
        /// buffer and it is potentially much more efficient.
        /// </code>
        /// </summary>
        /// <param name="src">The array from which bytes are to be read</param>
        /// <param name="offset">
        /// The offset within the array of the first byte to be read;
        /// must be non-negative and no larger than {@code array.length}
        /// </param>
        /// <param name="length">
        /// The number of bytes to be read from the given array;
        /// must be non-negative and no larger than
        /// {@code array.length - offset}
        /// </param>
        /// <returns>This buffer</returns>
        /// <exception cref="Buffer{T}.OverflowException">
        /// If there is insufficient space in this buffer
        /// </exception>
        /// <exception cref="IndexOutOfRangeException">
        /// If the preconditions on the {@code offset} and {@code length}
        /// parameters do not hold
        /// </exception>
        /// <exception cref="Buffer{T}.ReadOnlyException">
        /// If this buffer is read-only
        /// </exception>
        public ByteBuffer Put(Memory<byte> src, int offset, int length)
        {
            CheckBounds(offset, length, src.Length);
            if (length > Remaining)
                throw new OverflowException();

            src.Span.Slice(offset, length)
                .CopyTo(_buffer.Span.Slice(NextPutIndex(length), length));
            return this;
        }

        /// <summary>
        /// Relative bulk <i>put</i> method  <i>(optional operation)</i>.
        ///
        /// <p> This method transfers the entire content of the given source
        /// byte array into this buffer.  An invocation of this method of the
        /// form {@code dst.put(a)} behaves in exactly the same way as the
        /// invocation</p>
        ///
        /// <code>dst.put(a, 0, a.length)</code>
        /// </summary>
        /// <param name="src">The source array</param>
        /// <returns>This buffer</returns>
        /// <exception cref="Buffer{T}.OverflowException">
        /// If there is insufficient space in this buffer
        /// </exception>
        /// <exception cref="IndexOutOfRangeException">
        /// If the preconditions on the {@code offset} and {@code length}
        /// parameters do not hold
        /// </exception>
        /// <exception cref="Buffer{T}.ReadOnlyException">
        /// If this buffer is read-only
        /// </exception>
        public ByteBuffer Put(Memory<byte> src)
            => Put(src, 0, src.Length);

        /// <summary>
        /// Relative <i>put</i> method  <i>(optional operation)</i>.
        ///
        /// <p> Writes the given byte into this buffer at the current
        /// position, and then increments the position. </p>
        /// </summary>
        /// <param name="b">The byte to be written</param>
        /// <returns>This buffer</returns>
        /// <exception cref="Buffer{T}.OverflowException">
        /// If this buffer's current position is not smaller than its limit
        /// </exception>
        /// <exception cref="Buffer{T}.ReadOnlyException">
        /// If this buffer is read-only
        /// </exception>
        public ByteBuffer Put(byte b)
        {
            _buffer.Span[NextPutIndex()] = b;
            return this;
        }

        public ByteBuffer Put(sbyte b)
            => Put(BitConverter.GetBytes(b)[0]);

        /// <summary>
        /// Absolute <i>put</i> method  <i>(optional operation)</i>.
        ///
        /// <p> Writes the given byte into this buffer at the given index. </p>
        /// </summary>
        /// <param name="index">The index at which the byte will be written</param>
        /// <param name="b">The byte value to be written</param>
        /// <returns>This buffer</returns>
        /// <exception cref="IndexOutOfRangeException">
        /// If {@code index} is negative or not smaller than the buffer's limit
        /// </exception>
        /// <exception cref="Buffer{T}.ReadOnlyException">
        /// If this buffer is read-only
        /// </exception>
        public ByteBuffer Put(int index, byte b)
            => Position(index).Put(b);
        public ByteBuffer Put(int index, sbyte b)
            => Position(index).Put(b);

        public ByteBuffer PutShort(short s)
        {
            if(_bigEndian)
                BinaryPrimitives.WriteInt16BigEndian(_buffer.Span.Slice(NextPutIndex(2)), s);
            else
                BinaryPrimitives.WriteInt16LittleEndian(_buffer.Span.Slice(NextPutIndex(2)), s);
            return this;
        }

        public ByteBuffer PutShort(int index, short s)
            => Position(index).PutShort(s);

        public ByteBuffer PutShort(ushort s)
        {
            if (_bigEndian)
                BinaryPrimitives.WriteUInt16BigEndian(_buffer.Span.Slice(NextPutIndex(2)), s);
            else
                BinaryPrimitives.WriteUInt16LittleEndian(_buffer.Span.Slice(NextPutIndex(2)), s);
            return this;
        }

        public ByteBuffer PutShort(int index, ushort s)
            => Position(index).PutShort(s);

        public ByteBuffer PutInt(int i)
        {
            if (_bigEndian)
                BinaryPrimitives.WriteInt32BigEndian(_buffer.Span.Slice(NextPutIndex(4)), i);
            else
                BinaryPrimitives.WriteInt32LittleEndian(_buffer.Span.Slice(NextPutIndex(4)), i);
            return this;
        }

        public ByteBuffer PutInt(int index, int i)
            => Position(index).PutInt(i);

        public ByteBuffer PutLong(long l)
        {
            if (_bigEndian)
                BinaryPrimitives.WriteInt64BigEndian(_buffer.Span.Slice(NextPutIndex(8)), l);
            else
                BinaryPrimitives.WriteInt64LittleEndian(_buffer.Span.Slice(NextPutIndex(8)), l);
            return this;
        }

        public ByteBuffer PutLong(int index, long l)
            => Position(index).PutLong(l);
        #endregion

        #region Get ops

        /// <summary>
        /// Relative <i>get</i> method.  Reads the byte at this buffer's
        /// current position, and then increments the position.
        /// </summary>
        /// <returns>The byte at the buffer's current position</returns>
        /// <exception cref="Buffer{T}.UnderflowException">
        /// If the buffer's current position is not smaller than its limit
        /// </exception>
        public byte Get()
            => _buffer.Span[NextGetIndex()];

        /// <summary>
        /// Relative bulk <i>get</i> method.
        ///
        /// <p> This method transfers bytes from this buffer into the given
        /// destination array.  If there are fewer bytes remaining in the
        /// buffer than are required to satisfy the request, that is, if
        /// <paramref name="length"/> {@code >} <see cref="Buffer{T}.Remaining"/>, then no
        /// bytes are transferred and a <see cref="Buffer{T}.UnderflowException"/> is
        /// thrown.</p>
        ///
        /// <p> Otherwise, this method copies {@code length} bytes from this
        /// buffer into the given array, starting at the current position of this
        /// buffer and at the given offset in the array.  The position of this
        /// buffer is then incremented by <paramref name="length"/>.</p>
        /// 
        /// <p> In other words, an invocation of this method of the form
        /// <code>src.get(dst, off, len)</code> has exactly the same effect as
        /// the loop</p>
        ///
        /// <code>
        ///     <![CDATA[
        ///         for (int i = off; i < off + len; i++)
        ///             dst[i] = src.get();
        ///     ]]>
        /// </code>
        ///
        /// except that it first checks that there are sufficient bytes in
        /// this buffer and it is potentially much more efficient.
        /// </summary>
        /// <param name="dst">The array into which bytes are to be written</param>
        /// <param name="offset">
        /// The offset within the array of the first byte to be
        /// written; must be non-negative and no larger than <paramref name="length"></paramref>
        /// </param>
        /// <param name="length">
        /// The maximum number of bytes to be written to the given
        /// array; must be non-negative and no larger than
        /// <code>dst.length - <paramref name="offset"/></code>
        /// </param>
        /// <returns>This buffer</returns>
        /// <exception cref="Buffer{T}.UnderflowException">
        /// If there are fewer than <paramref name="length"/> bytes remaining in this buffer
        /// </exception>
        /// <exception cref="IndexOutOfRangeException">
        /// If the preconditions on the <paramref name="offset"/> and <paramref name="length"/>
        /// parameters do not hold
        /// </exception>
        public ByteBuffer Get(Memory<byte> dst, int offset, int length)
        {
            CheckBounds(offset, length, dst.Length);
            if(length > Remaining)
                throw new UnderflowException();
            _buffer.Span.Slice(NextGetIndex(length), length)
                .CopyTo(dst.Span.Slice(offset, length));
            return this;
        }

        /// <summary>
        /// Relative bulk <i>get</i> method.
        ///
        /// <p> This method transfers bytes from this buffer into the given
        /// destination array.  An invocation of this method of the form
        /// <code>src.get(a)</code> behaves in exactly the same way as the invocation </p>
        ///
        /// <code>src.get(a, 0, a.length)</code>
        /// </summary>
        /// <param name="dst">The destination array</param>
        /// <returns>This buffer</returns>
        /// <exception cref="Buffer{T}.UnderflowException">
        /// If there are fewer than {@code length} bytes remaining in this buffer
        /// </exception>
        public ByteBuffer Get(Memory<byte> dst) => Get(dst, 0, dst.Length);

        /// <summary>
        /// Absolute <i>get</i> method.  Reads the byte at the given index.
        /// </summary>
        /// <param name="index">The index from which the byte will be read</param>
        /// <returns>The byte at the given index</returns>
        /// <exception cref="IndexOutOfRangeException">
        /// If {@code index} is negative or not smaller than the buffer's limit
        /// </exception>
        public byte Get(int index)
            => Position(index).Get();

        public short GetShort()
        {
            return _bigEndian 
                ? BinaryPrimitives.ReadInt16BigEndian(_buffer.Span.Slice(NextGetIndex(2))) 
                : BinaryPrimitives.ReadInt16LittleEndian(_buffer.Span.Slice(NextGetIndex(2)));
        }

        public short GetShort(int index)
            => Position(index).GetShort();

        public ushort GetUShort()
        {
            return _bigEndian 
                ? BinaryPrimitives.ReadUInt16BigEndian(_buffer.Span.Slice(NextGetIndex(2))) 
                : BinaryPrimitives.ReadUInt16LittleEndian(_buffer.Span.Slice(NextGetIndex(2)));
        }

        public ushort GetUShort(int index)
            => Position(index).GetUShort();

        public int GetInt()
        {
            return _bigEndian
                ? BinaryPrimitives.ReadInt32BigEndian(_buffer.Span.Slice(NextGetIndex(4)))
                : BinaryPrimitives.ReadInt32LittleEndian(_buffer.Span.Slice(NextGetIndex(4)));
        }

        public int GetInt(int index)
            => Position(index).GetInt();

        public long GetLong()
        {
            return _bigEndian
                ? BinaryPrimitives.ReadInt64BigEndian(_buffer.Span.Slice(NextGetIndex(8)))
                : BinaryPrimitives.ReadInt64LittleEndian(_buffer.Span.Slice(NextGetIndex(8)));
        }

        public long GetLong(int index)
            => Position(index).GetLong();

        #endregion

        public void Dispose()
        {
            // This is important. Only destroy the backing byte array when the actual owner is disposed.
            // Any other instances deriving from the owner (from Slice() or Array property) do not own
            // their Memory<T> backing array and thus have a null _memoryOwner field.
            _memoryOwner?.Dispose();
        }
    }
}
#nullable restore
