//-----------------------------------------------------------------------
// <copyright file="ByteString.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    public sealed class ByteString : IEquatable<ByteString>, IEnumerable<byte>
    {
        #region creation methods

        /// <summary>
        /// INTERNAL API: remove this method once <see cref="Udp.Send"/> public constructor will be removed.
        /// </summary>
        internal static ByteString FromBuffers(IEnumerator<ByteBuffer> buffers)
        {
            var cached = new List<ByteBuffer>();
            while (buffers.MoveNext()) cached.Add(buffers.Current);
            return FromBytes(cached);
        }

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

            if (offset < 0 || offset >= array.Length) throw new ArgumentOutOfRangeException(nameof(offset), $"Provided offset of [{offset}] is outside bounds of an array [{array.Length}]");
            if (count > array.Length - offset) throw new ArgumentException($"Provided length [{count}] of array to copy doesn't fit array length [{array.Length}] within given offset [{offset}]", nameof(count));

            var copy = new byte[count];
            Array.Copy(array, offset, copy, 0, count);

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
            if (offset < 0 || (offset != 0 && offset >= array.Length)) throw new ArgumentOutOfRangeException(nameof(offset), $"Provided offset [{offset}] is outside bounds of an array");
            if (count > array.Length - offset) throw new ArgumentException($"Provided length of array to copy [{count}] doesn't fit array length [{array.Length}] and offset [{offset}].", nameof(count));

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

            var array = (buffers as ByteBuffer[]) ?? buffers.ToArray();
            var count = 0;
            foreach (var buffer in array)
                count += buffer.Count;

            return new ByteString(array, count);
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
        public static ByteString Empty { get; } = new ByteString(new ByteBuffer(new byte[0], 0, 0));

        #endregion

        private readonly int _count;
        private readonly ByteBuffer[] _buffers;

        private ByteString(ByteBuffer[] buffers, int count)
        {
            _buffers = buffers;
            _count = count;
        }

        private ByteString(ByteBuffer buffer)
        {
            _buffers = new[] { buffer };
            _count = buffer.Count;
        }

        private ByteString(byte[] array, int offset, int count)
        {
            _buffers = new[] { new ByteBuffer(array, offset, count) };
            _count = count;
        }

        /// <summary>
        /// Gets a total number of bytes stored inside this <see cref="ByteString"/>.
        /// </summary>
        public int Count => _count;

        /// <summary>
        /// Determines if current <see cref="ByteString"/> has compact representation.
        /// Compact byte strings represent bytes stored inside single, continuous
        /// block of memory.
        /// </summary>
        /// <returns>TBD</returns>
        public bool IsCompact => _buffers.Length == 1;

        /// <summary>
        /// Determines if current <see cref="ByteString"/> is empty.
        /// </summary>
        public bool IsEmpty => _count == 0;

        /// <summary>
        /// Gets sequence of the buffers used underneat.
        /// </summary>
        internal IList<ByteBuffer> Buffers => _buffers;

        /// <summary>
        /// Gets a byte stored under a provided <paramref name="index"/>.
        /// </summary>
        /// <param name="index">TBD</param>
        public byte this[int index]
        {
            get
            {
                if (index >= _count) throw new IndexOutOfRangeException("Requested index is outside of the bounds of the ByteString");
                int j;
                var i = GetBufferFittingIndex(index, out j);
                var buffer = _buffers[i];
                return buffer.Array[buffer.Offset + j];
            }
        }

        /// <summary>
        /// Compacts current <see cref="ByteString"/>, potentially copying its content underneat
        /// into new byte array.
        /// </summary>
        /// <returns>TBD</returns>
        public ByteString Compact()
        {
            if (IsCompact) return this;

            var copy = this.ToArray();
            return new ByteString(copy, 0, copy.Length);
        }

        /// <summary>
        /// Slices current <see cref="ByteString"/>, creating a new <see cref="ByteString"/>
        /// which contains a specified range of data from the original. This is non-copying
        /// operation.
        /// </summary>
        /// <param name="index">index inside current <see cref="ByteString"/>, from which slicing should start</param>
        public ByteString Slice(int index) => Slice(index, _count - index);

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
            //TODO: this is really stupid, but previous impl didn't throw if arguments 
            //      were out of range. We either have to round them to valid bounds or 
            //      (future version, provide negative-arg slicing like i.e. Python).
            if (index < 0) index = 0;
            if (index >= _count) index = Math.Max(0, _count - 1);
            if (count > _count - index) count = _count - index;
            if (count <= 0) return Empty;

            if (index == 0 && count == _count) return this;
            
            int j;
            var i = GetBufferFittingIndex(index, out j);
            var init = _buffers[i];

            var copied = Math.Min(init.Count - j, count);
            var newBuffers = new ByteBuffer[_buffers.Length - i];
            newBuffers[0] = new ByteBuffer(init.Array, init.Offset + j, copied);

            i++;
            var k = 1;
            for (; i < _buffers.Length; i++, k++)
            {
                if (copied >= count) break;

                var buffer = _buffers[i];
                var toCopy = Math.Min(buffer.Count, count - copied);
                newBuffers[k] = new ByteBuffer(buffer.Array, buffer.Offset, toCopy);
                copied += toCopy;
            }

            if (k < newBuffers.Length)
                newBuffers = newBuffers.Take(k).ToArray();

            return new ByteString(newBuffers, count);
        }

        /// <summary>
        /// Given an <paramref name="index"/> in current <see cref="ByteString"/> tries to 
        /// find which buffer will be used to contain that index and return its range.
        /// An offset within the buffer itself will be stored in <paramref name="indexWithinBuffer"/>.
        /// </summary>
        /// <param name="index"></param>
        /// <param name="indexWithinBuffer"></param>
        /// <returns></returns>
        private int GetBufferFittingIndex(int index, out int indexWithinBuffer)
        {
            if (index == 0)
            {
                indexWithinBuffer = 0;
                return 0;
            }

            var j = index;
            for (var i = 0; i < _buffers.Length; i++)
            {
                var buffer = _buffers[i];
                if (j >= buffer.Count)
                {
                    j -= buffer.Count;
                }
                else
                {
                    indexWithinBuffer = j;
                    return i;
                }
            }

            throw new IndexOutOfRangeException($"Requested index [{index}] is outside of the bounds of current ByteString.");
        }

        /// <summary>
        /// Returns an index of the first occurence of provided byte starting 
        /// from the beginning of the <see cref="ByteString"/>.
        /// </summary>
        /// <param name="b"></param>
        /// <returns></returns>
        public int IndexOf(byte b)
        {
            var idx = 0;
            foreach (var x in this)
            {
                if (x == b) return idx;
                idx++;
            }

            return -1;
        }

        /// <summary>
        /// Returns an index of the first occurence of provided byte starting 
        /// from the provided index.
        /// </summary>
        /// <returns></returns>
        public int IndexOf(byte b, int from)
        {
            if (from >= _count) return -1;

            int j;
            var i = GetBufferFittingIndex(from, out j);
            var idx = from;
            for (; i < _buffers.Length; i++)
            {
                var buffer = _buffers[i];
                for (; j < buffer.Count; j++, idx++)
                {
                    if (buffer.Array[buffer.Offset + j] == b) return idx;
                }
                j = 0;
            }

            return -1;
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
            // quick check: if subsequence is longer than remaining size, return false
            if (other.Count > _count - index) return false;

            int thisIdx = 0, otherIdx = 0;
            var i = GetBufferFittingIndex(index, out thisIdx);
            var j = 0;
            while (j < other._buffers.Length)
            {
                var buffer = _buffers[i];
                var otherBuffer = other._buffers[j];

                while (thisIdx < buffer.Count && otherIdx < otherBuffer.Count)
                {
                    if (buffer.Array[buffer.Offset + thisIdx] != otherBuffer.Array[otherBuffer.Offset + otherIdx])
                        return false;

                    thisIdx++;
                    otherIdx++;
                }

                if (thisIdx >= buffer.Count)
                {
                    i++;
                    thisIdx = 0;
                }
                if (otherIdx >= otherBuffer.Count)
                {
                    j++;
                    otherIdx = 0;
                }
            }

            return true;
        }

        /// <summary>
        /// Copies content of a current <see cref="ByteString"/> into a single byte array.
        /// </summary>
        /// <returns>TBD</returns>
        public byte[] ToArray()
        {
            var copy = new byte[_count];
            this.CopyTo(copy, 0, _count);
            return copy;
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
            if (this.IsEmpty) return other;

            var count = _count + other._count;
            var len1 = _buffers.Length;
            var len2 = other._buffers.Length;
            var array = new ByteBuffer[len1 + len2];
            Array.Copy(this._buffers, 0, array, 0, len1);
            Array.Copy(other._buffers, 0, array, len1, len2);

            return new ByteString(array, count);
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
            if (index < 0 || index >= buffer.Length) throw new ArgumentOutOfRangeException(nameof(index), "Provided index is outside the bounds of the buffer to copy to.");
            if (count > buffer.Length - index) throw new ArgumentException("Provided number of bytes to copy won't fit into provided buffer", nameof(count));

            count = Math.Min(count, _count);
            var remaining = count;
            var position = index;
            foreach (var b in _buffers)
            {
                var toCopy = Math.Min(b.Count, remaining);
                Array.Copy(b.Array, b.Offset, buffer, position, toCopy);
                position += toCopy;
                remaining -= toCopy;

                if (remaining == 0) return count;
            }

            return 0;
        }

        /// <summary>
        /// Copies content of the current <see cref="ByteString"/> to a provided 
        /// writeable <paramref name="stream"/>.
        /// </summary>
        /// <param name="stream"></param>
        public void WriteTo(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            foreach (var buffer in _buffers)
            {
                stream.Write(buffer.Array, buffer.Offset, buffer.Count);
            }
        }

        /// <summary>
        /// Asynchronously copies content of the current <see cref="ByteString"/> 
        /// to a provided writeable <paramref name="stream"/>.
        /// </summary>
        /// <param name="stream"></param>
        public async Task WriteToAsync(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            foreach (var buffer in _buffers)
            {
                await stream.WriteAsync(buffer.Array, buffer.Offset, buffer.Count);
            }
        }

        public override bool Equals(object obj) => Equals(obj as ByteString);

        public override int GetHashCode()
        {
            var hashCode = 0;
            foreach (var b in this)
            {
                hashCode = (hashCode * 397) ^ b.GetHashCode();
            }
            return hashCode;
        }

        public bool Equals(ByteString other)
        {
            if (ReferenceEquals(other, this)) return true;
            if (ReferenceEquals(other, null)) return false;
            if (_count != other._count) return false;

            using (var thisEnum = this.GetEnumerator())
            using (var otherEnum = other.GetEnumerator())
            {
                while (thisEnum.MoveNext() && otherEnum.MoveNext())
                {
                    if (thisEnum.Current != otherEnum.Current) return false;
                }
            }

            return true;
        }

        public IEnumerator<byte> GetEnumerator()
        {
            foreach (var buffer in _buffers)
            {
                for (int i = buffer.Offset; i < buffer.Offset + buffer.Count; i++)
                {
                    yield return buffer.Array[i];
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => ToString(Encoding.UTF8);

        public string ToString(Encoding encoding)
        {
            if (IsCompact)
                return encoding.GetString(_buffers[0].Array, _buffers[0].Offset, _buffers[0].Count);

            byte[] buffer = ToArray();

            return encoding.GetString(buffer);
        }

        public static bool operator ==(ByteString x, ByteString y) => Equals(x, y);

        public static bool operator !=(ByteString x, ByteString y) => !Equals(x, y);

        public static explicit operator ByteString(byte[] bytes) => ByteString.CopyFrom(bytes);
        
        public static explicit operator byte[] (ByteString byteString) => byteString.ToArray();
        
        public static ByteString operator +(ByteString x, ByteString y) => x.Concat(y);
    }
    
    public enum ByteOrder
    {
        BigEndian,
        LittleEndian
    }
}
