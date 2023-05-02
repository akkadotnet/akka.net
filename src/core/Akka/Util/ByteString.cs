//-----------------------------------------------------------------------
// <copyright file="ByteString.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Util.Internal;

namespace Akka.IO
{
    // TODO: Move to Akka.Util namespace - this will require changes as name clashes with ProtoBuf class
    using ByteBuffer = ArraySegment<byte>;

    /// <summary>
    /// A rope-like immutable data structure containing bytes.
    /// The goal of this structure is to reduce copying of arrays
    /// when concatenating and slicing sequences of bytes,
    /// and also providing a thread safe way of working with bytes.
    /// </summary>
    [DebuggerDisplay("(Count = {_count}, Buffers = {_buffers})")]
    public sealed class ByteString : IEquatable<ByteString>
    {
        #region creation methods

        /// <summary>
        /// Creates a new <see cref="ByteString"/> by copying a provided byte array.
        /// </summary>
        /// <param name="array">Array of bytes to copy</param>
        /// <returns>A byte string representation of array of bytes.</returns>
        public static ByteString CopyFrom(byte[] array) => CopyFrom(array, 0, array.Length);

        /// <summary>
        /// Creates a new <see cref="ByteString"/> by copying a byte array range from provided buffer.
        /// </summary>
        /// <param name="buffer">Buffer specifying a byte array range to copy.</param>
        /// <returns>A byte string representation of array of bytes.</returns>
        public static ByteString CopyFrom(ByteBuffer buffer) => CopyFrom(buffer.Array, buffer.Offset, buffer.Count);

        /// <summary>
        /// Creates a new <see cref="ByteString"/> by copying a byte array.
        /// </summary>
        /// <param name="array">Array of bytes to copy</param>
        /// <param name="offset">Index in provided <paramref name="array"/>, at which copy should start.</param>
        /// <param name="count">Number of bytes to copy.</param>
        /// <returns>TBD</returns>
        public static ByteString CopyFrom(byte[] array, int offset, int count)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));

            if (count == 0) return Empty;

            if (offset < 0 || offset >= array.Length)
                throw new ArgumentOutOfRangeException(nameof(offset),
                    $"Provided offset of [{offset}] is outside bounds of an array [{array.Length}]");
            if (count > array.Length - offset)
                throw new ArgumentException(
                    $"Provided length [{count}] of array to copy doesn't fit array length [{array.Length}] within given offset [{offset}]",
                    nameof(count));

            var copy = new byte[count];
            Array.Copy(array, offset, copy, 0, count);

            return new ByteString(copy, 0, copy.Length);
        }

        /// <summary>
        /// Creates a new <see cref="ByteString"/> by copying a <see cref="Memory{T}"/>.
        /// </summary>
        /// <param name="memory">The <see cref="Memory{T}"/> to copy</param>
        /// <returns>The new <see cref="ByteString"/></returns>
        public static ByteString CopyFrom(Memory<byte> memory)
            => CopyFrom(memory, 0, memory.Length);

        /// <summary>
        /// Creates a new <see cref="ByteString"/> by copying a <see cref="Memory{T}"/>.
        /// </summary>
        /// <param name="memory">The <see cref="Memory{T}"/> to copy</param>
        /// <param name="offset">Index in provided <paramref name="memory"/>, at which copy should start.</param>
        /// <param name="count">Number of bytes to copy.</param>
        /// <returns>The new <see cref="ByteString"/></returns>
        public static ByteString CopyFrom(Memory<byte> memory, int offset, int count)
        {
            if (count == 0) return Empty;

            if (offset < 0 || offset >= memory.Length)
                throw new ArgumentOutOfRangeException(nameof(offset),
                    $"Provided offset of [{offset}] is outside bounds of an array [{memory.Length}]");
            if (count > memory.Length - offset)
                throw new ArgumentException(
                    $"Provided length [{count}] of array to copy doesn't fit array length [{memory.Length}] within given offset [{offset}]",
                    nameof(count));

            var copy = new byte[count];
            memory.Slice(offset, count).CopyTo(copy);

            return new ByteString(copy, 0, copy.Length);
        }

        /// <summary>
        /// Creates a new <see cref="ByteString"/> by copying a <see cref="Span{T}"/>.
        /// </summary>
        /// <param name="span">The <see cref="Span{T}"/> to copy</param>
        /// <returns>The new <see cref="ByteString"/></returns>
        public static ByteString CopyFrom(Span<byte> span)
            => CopyFrom(span, 0, span.Length);

        /// <summary>
        /// Creates a new <see cref="ByteString"/> by copying a <see cref="Span{T}"/>.
        /// </summary>
        /// <param name="span">The <see cref="Span{T}"/> to copy</param>
        /// <param name="offset">Index in provided <paramref name="span"/>, at which copy should start.</param>
        /// <param name="count">Number of bytes to copy.</param>
        /// <returns>The new <see cref="ByteString"/></returns>
        public static ByteString CopyFrom(Span<byte> span, int offset, int count)
        {
            if (count == 0) return Empty;

            if (offset < 0 || offset >= span.Length)
                throw new ArgumentOutOfRangeException(nameof(offset),
                    $"Provided offset of [{offset}] is outside bounds of an array [{span.Length}]");
            if (count > span.Length - offset)
                throw new ArgumentException(
                    $"Provided length [{count}] of array to copy doesn't fit array length [{span.Length}] within given offset [{offset}]",
                    nameof(count));

            var copy = new byte[count];
            span.Slice(offset, count).CopyTo(copy);

            return new ByteString(copy, 0, copy.Length);
        }

        /// <summary>
        /// Creates a new <see cref="ByteString"/> by copying segments of bytes.
        /// </summary>
        /// <param name="buffers"></param>
        /// <returns></returns>
        public static ByteString CopyFrom(IEnumerable<ByteBuffer> buffers)
        {
            if (buffers == null) throw new ArgumentNullException(nameof(buffers));

            var array = (buffers as ByteBuffer[]) ?? buffers.ToArray();
            var count = 0;
            foreach (var buffer in array)
                count += buffer.Count;

            var copy = new byte[count];
            var position = 0;
            foreach (var buffer in array)
            {
                Array.Copy(buffer.Array, buffer.Offset, copy, position, buffer.Count);
                position += buffer.Count;
            }

            return new ByteString(copy, 0, count);
        }

        /// <summary>
        /// Creates a new <see cref="ByteString"/> by wrapping raw array of bytes.
        /// WARNING: this method doesn't copy underlying array, but expects 
        /// that it should not be modified once attached to byte string.
        /// </summary>
        /// <param name="array">TBD</param>
        /// <returns>TBD</returns>
        public static ByteString FromBytes(byte[] array) => FromBytes(array, 0, array.Length);

        /// <summary>
        /// Creates a new <see cref="ByteString"/> by wrapping raw range over array of bytes. WARNING: 
        /// this method doesn't copy underlying array, but expects that 
        /// represented range should not be modified once attached to byte string.
        /// </summary>
        /// <param name="buffer">TBD</param>
        /// <returns>TBD</returns>
        public static ByteString FromBytes(ArraySegment<byte> buffer) =>
            FromBytes(buffer.Array, buffer.Offset, buffer.Count);

        /// <summary>
        /// Creates a new <see cref="ByteString"/> by wrapping raw range over array of bytes. WARNING: 
        /// this method doesn't copy underlying array, but expects that 
        /// represented range should not be modified once attached to byte string.
        /// </summary>
        /// <param name="array">TBD</param>
        /// <param name="offset">TBD</param>
        /// <param name="count">TBD</param>
        /// <returns>TBD</returns>
        public static ByteString FromBytes(byte[] array, int offset, int count)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (offset < 0 || (offset != 0 && offset >= array.Length))
                throw new ArgumentOutOfRangeException(nameof(offset),
                    $"Provided offset [{offset}] is outside bounds of an array");
            if (count > array.Length - offset)
                throw new ArgumentException(
                    $"Provided length of array to copy [{count}] doesn't fit array length [{array.Length}] and offset [{offset}].",
                    nameof(count));

            if (count == 0) return Empty;

            return new ByteString(array, offset, count);
        }

        /// <summary>
        /// Creates a new <see cref="ByteString"/> by wrapping raw collection of byte segements. 
        /// WARNING: 
        /// this method doesn't copy underlying arrays, but expects that 
        /// represented range should not be modified once attached to byte string.
        /// </summary>
        public static ByteString FromBytes(IEnumerable<ByteBuffer> buffers)
        {
            if (buffers == null) throw new ArgumentNullException(nameof(buffers));

            // copy buffers into a new ReadOnlyMemory
            var buffersList = buffers.ToList();
            var newSize = buffersList.Sum(c => c.Count);
            var newBuffer = new byte[newSize];
            
            // copy all buffers into the new buffer
            var offset = 0;
            foreach (var buffer in buffersList)
            {
                Array.Copy(buffer.Array!, buffer.Offset, newBuffer, offset, buffer.Count);
                offset += buffer.Count;
            }
            
            return FromBytes(newBuffer);
        }

        /// <summary>
        /// Creates a new ByteString which will contain the UTF-8 representation of 
        /// the given String
        /// </summary>
        /// <param name="str">TBD</param>
        /// <returns>TBD</returns>
        public static ByteString FromString(string str) => FromString(str, Encoding.UTF8);

        /// <summary>
        /// Creates a new ByteString which will contain the representation of 
        /// the given String in the given charset encoding.
        /// </summary>
        /// <param name="str">TBD</param>
        /// <param name="encoding">TBD</param>
        /// <returns>TBD</returns>
        public static ByteString FromString(string str, Encoding encoding)
        {
            if (string.IsNullOrEmpty(str)) return Empty;

            var bytes = encoding.GetBytes(str);
            return FromBytes(bytes);
        }

        /// <summary>
        /// An empty <see cref="ByteString"/>.
        /// </summary>
        public static ByteString Empty { get; } = new(new ByteBuffer(Array.Empty<byte>(), 0, 0));

        #endregion

        private readonly ReadOnlyMemory<byte> _buffers;

        private ByteString(ByteBuffer buffer)
        {
            _buffers = buffer;
        }

        private ByteString(ReadOnlyMemory<byte> buffer)
        {
            _buffers = buffer;
        }

        private ByteString(byte[] array, int offset, int count)
        {
            _buffers = array.AsMemory(offset, count);
        }

        /// <summary>
        /// Gets a total number of bytes stored inside this <see cref="ByteString"/>.
        /// </summary>
        public int Count => _buffers.Length;

        /// <summary>
        /// Determines if current <see cref="ByteString"/> has compact representation.
        /// Compact byte strings represent bytes stored inside single, continuous
        /// block of memory.
        /// </summary>
        /// <returns>TBD</returns>
        public bool IsCompact => true;

        /// <summary>
        /// Determines if current <see cref="ByteString"/> is empty.
        /// </summary>
        public bool IsEmpty => Count == 0;

        /// <summary>
        /// Gets sequence of the buffers used underneath.
        /// </summary>
        internal ReadOnlyMemory<byte> Buffers => _buffers;

        /// <summary>
        /// Gets a byte stored under a provided <paramref name="index"/>.
        /// </summary>
        /// <param name="index">The index of the bytes we need access to</param>
        public byte this[int index]
        {
            get { return _buffers.Span[index]; }
        }

        /// <summary>
        /// Compacts current <see cref="ByteString"/>, potentially copying its content underneat
        /// into new byte array.
        /// </summary>
        /// <returns>TBD</returns>
        public ByteString Compact()
        {
            return this;
        }

        /// <summary>
        /// Slices current <see cref="ByteString"/>, creating a new <see cref="ByteString"/>
        /// which contains a specified range of data from the original. This is non-copying
        /// operation.
        /// </summary>
        /// <param name="index">index inside current <see cref="ByteString"/>, from which slicing should start</param>
        public ByteString Slice(int index) => Slice(index, Count - index);

        /// <summary>
        /// Slices current <see cref="ByteString"/>, creating a new <see cref="ByteString"/>
        /// which contains a specified range of data from the original. This is non-copying
        /// operation.
        /// </summary>
        /// <param name="index">index inside current <see cref="ByteString"/>, from which slicing should start</param>
        /// <param name="count">Number of bytes to fit into new <see cref="ByteString"/>.</param>
        /// <returns></returns>
        public ByteString Slice(int index, int count)
        {
            return new ByteString(_buffers.Slice(index, count));
            // //TODO: this is really stupid, but previous impl didn't throw if arguments 
            // //      were out of range. We either have to round them to valid bounds or 
            // //      (future version, provide negative-arg slicing like i.e. Python).
            // if (index < 0) index = 0;
            // if (index >= _count) index = Math.Max(0, _count - 1);
            // if (count > _count - index) count = _count - index;
            // if (count <= 0) return Empty;
            //
            // if (index == 0 && count == _count) return this;
            //
            // int j;
            // var i = GetBufferFittingIndex(index, out j);
            // var init = _buffers[i];
            //
            // var copied = Math.Min(init.Count - j, count);
            // var newBuffers = new ByteBuffer[_buffers.Length - i];
            // newBuffers[0] = new ByteBuffer(init.Array, init.Offset + j, copied);
            //
            // i++;
            // var k = 1;
            // for (; i < _buffers.Length; i++, k++)
            // {
            //     if (copied >= count) break;
            //
            //     var buffer = _buffers[i];
            //     var toCopy = Math.Min(buffer.Count, count - copied);
            //     newBuffers[k] = new ByteBuffer(buffer.Array, buffer.Offset, toCopy);
            //     copied += toCopy;
            // }
            //
            // if (k < newBuffers.Length)
            //     newBuffers = newBuffers.Take(k).ToArray();
            //
            // return new ByteString(newBuffers, count);
        }

        /// <summary>
        /// Returns an index of the first occurence of provided byte starting 
        /// from the beginning of the <see cref="ByteString"/>.
        /// </summary>
        /// <param name="b"></param>
        /// <returns></returns>
        public int IndexOf(byte b)
        {
            return IndexOf(b, 0);
        }

        /// <summary>
        /// Returns an index of the first occurence of provided byte starting 
        /// from the provided index.
        /// </summary>
        /// <returns></returns>
        public int IndexOf(byte b, int from)
        {
            return _buffers.Span.Slice(from).IndexOf(b);
        }

        /// <summary>
        /// Checks if a subsequence determined by the <paramref name="other"/> 
        /// byte string is can be found in current one, starting from provided 
        /// <paramref name="index"/>.
        /// </summary>
        /// <param name="other"></param>
        /// <param name="index"></param>
        /// <returns></returns>
        public bool HasSubstring(ByteString other, int index)
        {
            return _buffers.Span.Slice(index).IndexOfAny(other.Buffers.Span) >= 0;
        }

        /// <summary>
        /// Copies content of a current <see cref="ByteString"/> into a single byte array.
        /// </summary>
        /// <returns>TBD</returns>
        public byte[] ToArray()
        {
            if (Count == 0)
                return Array.Empty<byte>();

            return _buffers.ToArray();
        }

        /// <summary>
        /// Appends <paramref name="other"/> <see cref="ByteString"/> at the tail
        /// of a current one, creating a new <see cref="ByteString"/> in result.
        /// Contents of byte strings are NOT copied.
        /// </summary>
        /// <returns>TBD</returns>
        public ByteString Concat(ByteString other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other), "Cannot append null to ByteString.");

            if (other.IsEmpty) return this;
            if (IsEmpty) return other;


            var count = Count + other.Count;
            var dest = new byte[count];
            Buffers.CopyTo(dest);
            other.Buffers.Span.CopyTo(dest.AsSpan(other.Count));
            return new ByteString(dest);
        }

        /// <summary>
        /// Copies content of a current <see cref="ByteString"/> into a provided
        /// <paramref name="buffer"/> starting from <paramref name="index"/> in that
        /// buffer and copying a <paramref name="count"/> number of bytes.
        /// </summary>
        /// <returns>TBD</returns>
        public int CopyTo(byte[] buffer, int index, int count)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (index < 0 || index >= buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(index),
                    "Provided index is outside the bounds of the buffer to copy to.");
            if (count > buffer.Length - index)
                throw new ArgumentException("Provided number of bytes to copy won't fit into provided buffer",
                    nameof(count));

            var span = buffer.AsSpan();
            return CopyTo(ref span, index, count);
        }

        /// <summary>
        /// Copies content of a current <see cref="ByteString"/> into a provided <see cref="Memory{T}"/>
        /// <paramref name="buffer"/>
        /// </summary>
        /// <returns>The number of bytes copied</returns>
        public int CopyTo(ref Memory<byte> buffer)
            => CopyTo(ref buffer, 0, buffer.Length);

        /// <summary>
        /// Copies content of a current <see cref="ByteString"/> into a provided <see cref="Memory{T}"/>
        /// <paramref name="buffer"/> starting from <paramref name="index"/> in that
        /// buffer and copying a <paramref name="count"/> number of bytes.
        /// </summary>
        /// <returns>The number of bytes copied</returns>
        public int CopyTo(ref Memory<byte> buffer, int index, int count)
        {
            if (index < 0 || index >= buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(index),
                    "Provided index is outside the bounds of the buffer to copy to.");
            if (count > buffer.Length - index)
                throw new ArgumentException("Provided number of bytes to copy won't fit into provided buffer",
                    nameof(count));

            var span = buffer.Span;
            return CopyTo(ref span, index, count);
        }

        /// <summary>
        /// Copies content of a current <see cref="ByteString"/> into a provided <see cref="Span{T}"/>
        /// <paramref name="buffer"/>.
        /// </summary>
        /// <returns>The number of bytes copied</returns>
        public int CopyTo(ref Span<byte> buffer)
            => CopyTo(ref buffer, 0, buffer.Length);

        /// <summary>
        /// Copies content of a current <see cref="ByteString"/> into a provided <see cref="Span{T}"/>
        /// <paramref name="buffer"/> starting from <paramref name="index"/> in that
        /// buffer and copying a <paramref name="count"/> number of bytes.
        /// </summary>
        /// <returns>The number of bytes copied</returns>
        public int CopyTo(ref Span<byte> buffer, int index, int count)
        {
            if (index < 0 || index >= buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(index),
                    "Provided index is outside the bounds of the buffer to copy to.");
            if (count > buffer.Length - index)
                throw new ArgumentException("Provided number of bytes to copy won't fit into provided buffer",
                    nameof(count));

            var span = Buffers.Span;
            var actualCount = Math.Min(count, Count);

            span.Slice(0, actualCount).CopyTo(buffer.Slice(index));

            return actualCount;
        }

        /// <summary>
        /// Copies content of the current <see cref="ByteString"/> to a provided 
        /// writeable <paramref name="stream"/>.
        /// </summary>
        /// <param name="stream"></param>
        public void WriteTo(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

#if NETSTANDARD2_0
            stream.Write(Buffers.ToArray(), 0, Buffers.Length);
#else
            stream.Write(Buffers.Span);
#endif
        }

        /// <summary>
        /// Asynchronously copies content of the current <see cref="ByteString"/> 
        /// to a provided writeable <paramref name="stream"/>.
        /// </summary>
        /// <param name="stream"></param>
        public async Task WriteToAsync(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

#if NETSTANDARD2_0
            await stream.WriteAsync(Buffers.ToArray(), 0, Buffers.Length);
#else
            await stream.WriteAsync(Buffers);
#endif
        }

        public override bool Equals(object obj) => Equals(obj as ByteString);

        public override int GetHashCode()
        {
            return Buffers.GetHashCode();
        }

        public bool Equals(ByteString other)
        {
            if (ReferenceEquals(other, this)) return true;
            if (ReferenceEquals(other, null)) return false;
            if (Count != other.Count) return false;

            var span = Buffers.Span;
            var otherSpan = other.Buffers.Span;
            return span.SequenceEqual(otherSpan);
        }

        public override string ToString() => ToString(Encoding.UTF8);

        public string ToString(Encoding encoding)
        {
#if NETSTANDARD2_0
            return encoding.GetString(Buffers.ToArray());
#else
            return encoding.GetString(Buffers.Span);
#endif
        }

        public static bool operator ==(ByteString x, ByteString y) => Equals(x, y);

        public static bool operator !=(ByteString x, ByteString y) => !Equals(x, y);

        public static explicit operator ByteString(byte[] bytes) => ByteString.CopyFrom(bytes);

        public static explicit operator byte[](ByteString byteString) => byteString.ToArray();

        public static ByteString operator +(ByteString x, ByteString y) => x.Concat(y);
    }

    public enum ByteOrder
    {
        BigEndian,
        LittleEndian
    }
}