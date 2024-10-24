//-----------------------------------------------------------------------
// <copyright file="ByteString.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    [DebuggerDisplay("(Count = {Count}, Buffer = {Memory})")]
    public sealed class ByteString : IEquatable<ByteString>
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

            if (offset < 0 || offset >= memory.Length) throw new ArgumentOutOfRangeException(nameof(offset), $"Provided offset of [{offset}] is outside bounds of an array [{memory.Length}]");
            if (count > memory.Length - offset) throw new ArgumentException($"Provided length [{count}] of array to copy doesn't fit array length [{memory.Length}] within given offset [{offset}]", nameof(count));

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

            if (offset < 0 || offset >= span.Length) throw new ArgumentOutOfRangeException(nameof(offset), $"Provided offset of [{offset}] is outside bounds of an array [{span.Length}]");
            if (count > span.Length - offset) throw new ArgumentException($"Provided length [{count}] of array to copy doesn't fit array length [{span.Length}] within given offset [{offset}]", nameof(count));

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
        public static ByteString Empty { get; } = new(new ByteBuffer(Array.Empty<byte>(), 0, 0));

        #endregion

        private readonly ReadOnlyMemory<byte> _memory;
        
        public ReadOnlyMemory<byte> Memory => _memory;

        private static ReadOnlyMemory<byte> ConvertToMemory(ByteBuffer[] buffers)
        {
            return buffers.Length switch
            {
                0 => ReadOnlyMemory<byte>.Empty,
                1 => new ReadOnlyMemory<byte>(buffers[0].Array, buffers[0].Offset, buffers[0].Count),
                _ => new ReadOnlyMemory<byte>(ConcatBuffers(buffers))
            };
            
            static byte[] ConcatBuffers(ByteBuffer[] buffers)
            {
                var count = buffers.Sum(x => x.Count);
                var result = new byte[count];
                var offset = 0;
                foreach (var buffer in buffers)
                {
                    Array.Copy(buffer.Array ?? throw new InvalidOperationException(), buffer.Offset, result, offset, buffer.Count);
                    offset += buffer.Count;
                }

                return result;
            }
        }

        private ByteString(ByteBuffer[] buffers, int count) : this(ConvertToMemory(buffers))
        {
        }

        private ByteString(ByteBuffer buffer) :
            this(new ReadOnlyMemory<byte>(buffer.Array, buffer.Offset, buffer.Count))
        {
        }

        private ByteString(byte[] array, int offset, int count) : this(new ReadOnlyMemory<byte>(array, offset, count))
        {
        }
        
        private ByteString(in ReadOnlyMemory<byte> memory)
        {
            _memory = memory;
        }

        /// <summary>
        /// Gets a total number of bytes stored inside this <see cref="ByteString"/>.
        /// </summary>
        public int Count => _memory.Length;

        /// <summary>
        /// Determines if current <see cref="ByteString"/> has compact representation.
        /// Compact byte strings represent bytes stored inside single, continuous
        /// block of memory.
        /// </summary>
        [Obsolete("This property will be removed in future versions of Akka.NET.")]
        public bool IsCompact => true;

        /// <summary>
        /// Determines if current <see cref="ByteString"/> is empty.
        /// </summary>
        public bool IsEmpty => Count == 0;

        /// <summary>
        /// Gets a byte stored under a provided <paramref name="index"/>.
        /// </summary>
        /// <param name="index">TBD</param>
        public byte this[int index]
        {
            get
            {
                return _memory.Span[index];
            }
        }

        /// <summary>
        /// Compacts current <see cref="ByteString"/>, potentially copying its content underneat
        /// into new byte array.
        /// </summary>
        [Obsolete("This method will be removed in future versions of Akka.NET.")]
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
        /// <exception cref="ArgumentOutOfRangeException">If index or count result in an invalid <see cref="ByteString"/>.</exception>
        public ByteString Slice(int index, int count)
        {
            return new ByteString(_memory.Slice(index, count));
        }

        /// <summary>
        /// Returns an index of the first occurence of provided byte starting 
        /// from the beginning of the <see cref="ByteString"/>.
        /// </summary>
        /// <param name="b"></param>
        /// <returns></returns>
        public int IndexOf(byte b)
        {
            return _memory.Span.IndexOf(b);
        }

        /// <summary>
        /// Returns an index of the first occurence of provided byte starting 
        /// from the provided index.
        /// </summary>
        /// <returns></returns>
        public int IndexOf(byte b, int from)
        {
            var rValue = _memory.Span[from..].IndexOf(b);
            return rValue == -1 ? -1 : rValue + from;
        }
        
        public int IndexOf(ByteString other, int index = 0)
        {
            if (other.Count == 0) return index; // Empty spans are always "found".
            if (index < 0 || index > Count) throw new ArgumentOutOfRangeException(nameof(index), "Start index is out of range.");
            if (Count - index < other.Count) return -1; // Can't find if `toFind` is larger considering the start index.
            
            var span = _memory.Span;
            var otherSpan = other._memory.Span;

            var indexOf = span.Slice(index).IndexOf(otherSpan);
            return indexOf == -1 ? -1 : indexOf + index;
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
            return IndexOf(other, index) > -1;
        }

        /// <summary>
        /// Copies content of a current <see cref="ByteString"/> into a single byte array.
        /// </summary>
        /// <remarks>
        /// WARNING: this method allocates!
        /// </remarks>
        /// <returns>A new array of data</returns>
        public byte[] ToArray()
        {
            return _memory.ToArray();
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

            // combine the two ReadOnlyMemory<byte> instances
            var array = new byte[_memory.Length + other._memory.Length];
            _memory.Span.CopyTo(array);
            other._memory.Span.CopyTo(array.AsSpan(_memory.Length));
            
            return new ByteString(array);
        }

        /// <summary>
        /// Copies content of a current <see cref="ByteString"/> into a provided
        /// <paramref name="buffer"/> starting from <paramref name="index"/> in that
        /// buffer and copying a <paramref name="count"/> number of bytes.
        /// </summary>
        /// <returns>TBD</returns>
        public int CopyTo(byte[] buffer, int index, int count)
        {
            if(buffer.Length == 0 && count == 0) return 0; // edge case for no-copy
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (index < 0 || index >= buffer.Length) throw new ArgumentOutOfRangeException(nameof(index), "Provided index is outside the bounds of the buffer to copy to.");
            if (count > buffer.Length - index) throw new ArgumentException("Provided number of bytes to copy won't fit into provided buffer", nameof(count));

            count = Math.Min(count, Count);
            _memory.Span.CopyTo(buffer.AsSpan(index, count));
            return count;
        }

        /// <summary>
        /// Copies content of a current <see cref="ByteString"/> into a provided <see cref="Memory{T}"/>
        /// <paramref name="buffer"/>
        /// </summary>
        /// <returns>The number of bytes copied</returns>
        [Obsolete("This method will be removed in future versions of Akka.NET.")]
        public int CopyTo(ref Memory<byte> buffer)
            => CopyTo(ref buffer, 0, buffer.Length);
        
        /// <summary>
        /// Copies content of a current <see cref="ByteString"/> into a provided <see cref="Memory{T}"/>
        /// <paramref name="buffer"/> starting from <paramref name="index"/> in that
        /// buffer and copying a <paramref name="count"/> number of bytes.
        /// </summary>
        /// <returns>The number of bytes copied</returns>
        [Obsolete("This method will be removed in future versions of Akka.NET.")]
        public int CopyTo(ref Memory<byte> buffer, int index, int count)
        {
            if(buffer.Length == 0 && count == 0) return 0; // edge case for no-copy
            if (index < 0 || index >= buffer.Length) throw new ArgumentOutOfRangeException(nameof(index), "Provided index is outside the bounds of the buffer to copy to.");
            if (count > buffer.Length - index) throw new ArgumentException("Provided number of bytes to copy won't fit into provided buffer", nameof(count));

            count = Math.Min(count, Count);
            _memory.Span.Slice(0, count).CopyTo(buffer.Span.Slice(index));
            return count;
        }

        /// <summary>
        /// Copies content of a current <see cref="ByteString"/> into a provided <see cref="Span{T}"/>
        /// <paramref name="buffer"/>.
        /// </summary>
        /// <returns>The number of bytes copied</returns>
        [Obsolete("This method will be removed in future versions of Akka.NET.")]
        public int CopyTo(ref Span<byte> buffer)
            => CopyTo(ref buffer, 0, buffer.Length);
        
        /// <summary>
        /// Copies content of a current <see cref="ByteString"/> into a provided <see cref="Span{T}"/>
        /// <paramref name="buffer"/> starting from <paramref name="index"/> in that
        /// buffer and copying a <paramref name="count"/> number of bytes.
        /// </summary>
        /// <returns>The number of bytes copied</returns>
        [Obsolete("This method will be removed in future versions of Akka.NET.")]
        public int CopyTo(ref Span<byte> buffer, int index, int count)
        {
            if(buffer.Length == 0 && count == 0) return 0; // edge case for no-copy
            if (index < 0 || index >= buffer.Length) throw new ArgumentOutOfRangeException(nameof(index), "Provided index is outside the bounds of the buffer to copy to.");
            if (count > buffer.Length - index) throw new ArgumentException("Provided number of bytes to copy won't fit into provided buffer", nameof(count));

            count = Math.Min(count, Count);
            _memory.Span.Slice(0, count).CopyTo(buffer.Slice(index));
            return count;
        }

        /// <summary>
        /// Copies content of the current <see cref="ByteString"/> to a provided 
        /// writeable <paramref name="stream"/>.
        /// </summary>
        /// <param name="stream"></param>
        [Obsolete("This method will be removed in future versions of Akka.NET.")]
        public void WriteTo(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            
            // TODO: remove this method in future versions of Akka.NET and use System.Memory APIs
            var array = _memory.ToArray();
            stream.Write(array, 0, array.Length);
        }

        /// <summary>
        /// Asynchronously copies content of the current <see cref="ByteString"/> 
        /// to a provided writeable <paramref name="stream"/>.
        /// </summary>
        /// <param name="stream"></param>
        [Obsolete("This method will be removed in future versions of Akka.NET.")]
        public Task WriteToAsync(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            // TODO: remove this method in future versions of Akka.NET and use System.Memory APIs
            var array = _memory.ToArray();
            return stream.WriteAsync(array, 0, array.Length);
        }

        public override bool Equals(object obj) => Equals(obj as ByteString);

        public override int GetHashCode()
        {
            return _memory.GetHashCode();
        }

        public bool Equals(ByteString other)
        {
            if (ReferenceEquals(other, this)) return true;
            if (ReferenceEquals(other, null)) return false;
            
            return _memory.Span.SequenceEqual(other._memory.Span);
        }

        public override string ToString() => ToString(Encoding.UTF8);

        public string ToString(Encoding encoding)
        {
            // get span as byte array
            return encoding.GetString(_memory.Span.ToArray());
        }

        public static bool operator ==(ByteString x, ByteString y) => Equals(x, y);

        public static bool operator !=(ByteString x, ByteString y) => !Equals(x, y);

        public static explicit operator ByteString(byte[] bytes) => CopyFrom(bytes);
        
        public static explicit operator byte[] (ByteString byteString) => byteString.ToArray();
        
        public static explicit operator ByteString(Memory<byte> memory) => CopyFrom(memory);

        public static explicit operator ByteString(ReadOnlyMemory<byte> memory) => new(memory);
        
        public static ByteString operator +(ByteString x, ByteString y) => x.Concat(y);
    }
    
    public enum ByteOrder
    {
        BigEndian,
        LittleEndian
    }
}
