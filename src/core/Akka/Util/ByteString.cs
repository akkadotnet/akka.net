//-----------------------------------------------------------------------
// <copyright file="ByteString.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.IO
{
    // TODO: Move to Akka.Util namespace - this will require changes as name clashes with ProtoBuf class

    /// <summary>
    /// TBD
    /// </summary>
    partial /*object*/ class ByteString
    {
        private static ByteString Create(ByteString1 b, ByteStrings bs)
        {
            switch (Compare(b, bs))
            {
                case 3:
                    return new ByteStrings(new LinkedList<ByteString1>(bs.Items).AddFirst(b).List.ToArray(),
                        bs.Count + b.Count);
                case 2:
                    return bs;
                case 1:
                    return b;
                case 0:
                    return Empty;
            }
            throw new ArgumentOutOfRangeException();
        }

        private static int Compare(ByteString b1, ByteString b2)
        {
            if (b1.IsEmpty)
                return b2.IsEmpty ? 0 : 2;
            return b2.IsEmpty ? 1 : 3;
        }

        /// <summary>
        /// Creates a new ByteString by copying a byte array.
        /// </summary>
        /// <param name="array">TBD</param>
        /// <returns>TBD</returns>
        public ByteString FromArray(byte[] array)
        {
            return new ByteString1C((byte[]) array.Clone());
        }

        /// <summary>
        /// Creates a new ByteString by copying length bytes starting at offset from
        /// an Array.
        /// </summary>
        /// <param name="array">TBD</param>
        /// <param name="offset">TBD</param>
        /// <param name="length">TBD</param>
        /// <returns>TBD</returns>
        public ByteString FromArray(byte[] array, int offset, int length)
        {
            return CompactByteString.FromArray(array, offset, length);
        }

        /// <summary>
        /// Creates a new ByteString which will contain the UTF-8 representation of the given String
        /// </summary>
        /// <param name="str">TBD</param>
        /// <returns>TBD</returns>
        public static ByteString FromString(string str)
        {
            return FromString(str, Encoding.UTF8);
        }

        /// <summary>
        /// Creates a new ByteString which will contain the representation of the given String in the given charset
        /// </summary>
        /// <param name="str">TBD</param>
        /// <param name="encoding">TBD</param>
        /// <returns>TBD</returns>
        public static ByteString FromString(string str, Encoding encoding)
        {
            return CompactByteString.FromString(str, encoding);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="buffer">TBD</param>
        /// <returns>TBD</returns>
        public static ByteString FromByteBuffer(ByteBuffer buffer)
        {
            return Create(buffer);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public static readonly ByteString Empty = CompactByteString.EmptyCompactByteString;

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public static ByteStringBuilder NewBuilder()
        {
            return new ByteStringBuilder();
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal class ByteString1C : CompactByteString
        {
            private readonly byte[] _bytes;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="bytes">TBD</param>
            public ByteString1C(byte[] bytes)
            {
                _bytes = bytes;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="idx">TBD</param>
            public override byte this[int idx]
            {
                get { return _bytes[idx]; }
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns>TBD</returns>
            public sealed override ByteBuffer AsByteBuffer()
            {
                return new ByteString1(_bytes).AsByteBuffer();
            }

            /// <summary>
            /// TBD
            /// </summary>
            public override int Count
            {
                get { return _bytes.Length; }
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns>TBD</returns>
            public override ByteIterator Iterator()
            {
                return new ByteIterator.ByteArrayIterator(_bytes, 0, _bytes.Length);
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns>TBD</returns>
            public override IEnumerator<byte> GetEnumerator()
            {
                return ((IEnumerable<byte>) _bytes).GetEnumerator();
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns>TBD</returns>
            internal ByteString1 ToByteString1()
            {
                return new ByteString1(_bytes);
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="that">TBD</param>
            /// <returns>TBD</returns>
            public override ByteString Concat(ByteString that)
            {
                if (that.IsEmpty) return this;
                if (this.IsEmpty) return that;
                return ToByteString1() + that;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="from">TBD</param>
            /// <param name="until">TBD</param>
            /// <returns>TBD</returns>
            public override ByteString Slice(int from, int until)
            {
                return (from != 0 || until != Count)
                    ? ToByteString1().Slice(from, until)
                    : this;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="charset">TBD</param>
            /// <returns>TBD</returns>
            public override string DecodeString(Encoding charset)
            {
                return IsEmpty ? string.Empty : charset.GetString(_bytes);

            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal class ByteString1 : ByteString
        {
            private readonly byte[] _bytes;
            private readonly int _startIndex;
            private readonly int _length;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="bytes">TBD</param>
            /// <param name="startIndex">TBD</param>
            /// <param name="length">TBD</param>
            /// <returns>TBD</returns>
            public ByteString1(byte[] bytes, int startIndex, int length)
            {
                _bytes = bytes;
                _startIndex = startIndex;
                _length = length;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="bytes">TBD</param>
            /// <returns>TBD</returns>
            public ByteString1(byte[] bytes)
                : this(bytes, 0, bytes.Length)
            {
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="idx">TBD</param>
            /// <exception cref="IndexOutOfRangeException">TBD</exception>
            public override byte this[int idx]
            {
                get { return _bytes[checkRangeConvert(idx)]; }
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns>TBD</returns>
            public sealed override ByteBuffer AsByteBuffer()
            {
                return ByteBuffer.Wrap(_bytes, _startIndex, _length);
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns>TBD</returns>
            public override ByteIterator Iterator()
            {
                return new ByteIterator.ByteArrayIterator(_bytes, _startIndex, _startIndex + _length);
            }

            private int checkRangeConvert(int index)
            {
                if (0 <= index && _length > index)
                    return index + _startIndex;
                throw new IndexOutOfRangeException(index.ToString());
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns>TBD</returns>
            public override bool IsCompact()
            {
                return _length == _bytes.Length;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns>TBD</returns>
            public override CompactByteString Compact()
            {
                return IsCompact()
                    ? new ByteString1C(_bytes)
                    : new ByteString1C(ToArray());
            }

            /// <summary>
            /// TBD
            /// </summary>
            public override int Count
            {
                get { return _length; }
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="that">TBD</param>
            /// <exception cref="InvalidOperationException">TBD</exception>
            /// <returns>TBD</returns>
            public override ByteString Concat(ByteString that)
            {
                if (that.IsEmpty) return this;
                if (this.IsEmpty) return that;

                var b1C = that as ByteString1C;
                if (b1C != null)
                    return new ByteStrings(this, b1C.ToByteString1());

                var b1 = that as ByteString1;
                if (b1 != null)
                {
                    if (_bytes == b1._bytes && (_startIndex + _length == b1._startIndex))
                        return new ByteString1(_bytes, _startIndex, _length + b1._length);
                    return new ByteStrings(this, b1);
                }

                var bs = that as ByteStrings;
                if (bs != null)
                {
                    return Create(this, bs);
                }

                throw new InvalidOperationException();
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="charset">TBD</param>
            /// <returns>TBD</returns>
            public override string DecodeString(Encoding charset)
            {
                return charset.GetString(_length == _bytes.Length ? _bytes : ToArray());
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns>TBD</returns>
            public override IEnumerator<byte> GetEnumerator()
            {
                return _bytes.Skip(_startIndex).Take(_length).GetEnumerator();
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal class ByteStrings : ByteString
        {
            private readonly ByteString1[] _byteStrings;
            private readonly int _length;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="byteStrings">TBD</param>
            public ByteStrings(params ByteString1[] byteStrings)
                : this(byteStrings, byteStrings.Sum(x => x.Count))
            {
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="byteStrings">TBD</param>
            /// <param name="length">TBD</param>
            public ByteStrings(ByteString1[] byteStrings, int length)
            {
                _byteStrings = byteStrings;
                _length = length;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="idx">TBD</param>
            /// <exception cref="IndexOutOfRangeException">TBD</exception>
            public override byte this[int idx]
            {
                get
                {
                    if (0 <= idx && idx < Count)
                    {
                        var pos = 0;
                        var seen = 0;
                        while (idx >= seen + _byteStrings[pos].Count)
                        {
                            seen += _byteStrings[pos].Count;
                            pos += 1;
                        }
                        return _byteStrings[pos][idx - seen];
                    }
                    throw new IndexOutOfRangeException();
                }
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns>TBD</returns>
            public sealed override ByteBuffer AsByteBuffer()
            {
                return Compact().AsByteBuffer();
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns>TBD</returns>
            public override ByteIterator Iterator()
            {
                return new ByteIterator.MultiByteIterator(
                        _byteStrings.Select(x => (ByteIterator.ByteArrayIterator) x.Iterator()).ToArray());
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns>TBD</returns>
            public override IEnumerator<byte> GetEnumerator()
            {
                return _byteStrings.SelectMany(byteString => byteString).GetEnumerator();
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="that">TBD</param>
            /// <exception cref="InvalidOperationException">
            /// This exception is thrown if this <see cref="ByteString"/> cannot be concatenated with <paramref name="that"/>.
            /// </exception>
            /// <returns>TBD</returns>
            public override ByteString Concat(ByteString that)
            {
                if (that.IsEmpty)
                {
                    return this;
                }
                else if (this.IsEmpty)
                {
                    return that;
                }
                else
                {
                    var b1c = that as ByteString1C;
                    if (b1c != null)
                    {
                        return new ByteStrings(Items.Concat(b1c.ToByteString1()).ToArray());
                    }

                    var b1 = that as ByteString1;
                    if (b1 != null)
                    {
                        return new ByteStrings(Items.Concat(b1).ToArray());
                    }

                    var bs = that as ByteStrings;
                    if (bs != null)
                    {
                        return new ByteStrings(Items.Concat(bs.Items).ToArray());
                    }

                    throw new InvalidOperationException($"No suitable implementation found for concatenating ByteString of type {that.GetType()}");
                }
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns>TBD</returns>
            public override bool IsCompact()
            {
                return _byteStrings.Length == 1 && _byteStrings.Head().IsCompact();
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns>TBD</returns>
            public override CompactByteString Compact()
            {
                if (IsCompact())
                {
                    return _byteStrings.Head().Compact();
                }

                var bb = ByteBuffer.Allocate(Count);
                foreach (var item in Items)
                {
                    bb.Put(item.ToArray());
                }

                return new ByteString1C(bb.Array());
            }

            /// <summary>
            /// TBD
            /// </summary>
            public override int Count
            {
                get { return _length; }
            }

            /// <summary>
            /// TBD
            /// </summary>
            internal ByteString1[] Items
            {
                get { return _byteStrings; }
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="charset">TBD</param>
            /// <returns>TBD</returns>
            public override string DecodeString(Encoding charset)
            {
                return Compact().DecodeString(charset);
            }
        }
    }

    /// <summary>
    /// A rope-like immutable data structure containing bytes.
    /// The goal of this structure is to reduce copying of arrays
    /// when concatenating and slicing sequences of bytes,
    /// and also providing a thread safe way of working with bytes.
    /// </summary>
    public abstract partial class ByteString : IReadOnlyList<byte>, IEquatable<ByteString>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="index">TBD</param>
        public abstract byte this[int index] { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public abstract ByteBuffer AsByteBuffer();

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        protected virtual ByteStringBuilder newBuilder()
        {
            return NewBuilder();
        }


        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public abstract ByteIterator Iterator();

        /// <summary>
        /// TBD
        /// </summary>
        public Byte Head
        {
            get { return this[0]; }
        }
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public ByteString Tail()
        {
            return Drop(1);
        }
        /// <summary>
        /// TBD
        /// </summary>
        public byte Last
        {
            get { return this[Count - 1]; }
        }
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public ByteString Init()
        {
            return DropRight(1);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="from">TBD</param>
        /// <param name="until">TBD</param>
        /// <returns>TBD</returns>
        public virtual ByteString Slice(int @from, int until)
        {
            if (@from == 0 && until == Count) return this;
            return Iterator().Slice(@from, until).ToByteString();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="n">TBD</param>
        /// <returns>TBD</returns>
        public ByteString Take(int n)
        {
            return Slice(0, n);
        }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="n">TBD</param>
        /// <returns>TBD</returns>
        public ByteString TakeRight(int n)
        {
            return Slice(Count - n, Count);
        }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="n">TBD</param>
        /// <returns>TBD</returns>
        public ByteString Drop(int n)
        {
            return Slice(n, Count);
        }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="n">TBD</param>
        /// <returns>TBD</returns>
        public ByteString DropRight(int n)
        {
            return Slice(0, Count - n);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="p">TBD</param>
        /// <returns>TBD</returns>
        public ByteString TakeWhile(Func<byte, bool> p)
        {
            return Iterator().TakeWhile(p).ToByteString();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="p">TBD</param>
        /// <returns>TBD</returns>
        public ByteString DropWhile(Func<byte, bool> p)
        {
            return Iterator().DropWhile(p).ToByteString();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="p">TBD</param>
        /// <returns>TBD</returns>
        public Tuple<ByteString, ByteString> Span(Func<byte, bool> p)
        {
            var span = Iterator().Span(p);
            return Tuple.Create(span.Item1.ToByteString(), span.Item2.ToByteString());
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="n">TBD</param>
        /// <returns>TBD</returns>
        public Tuple<ByteString, ByteString> SplitAt(int n)
        {
            return Tuple.Create(Take(n), Drop(n));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="p">TBD</param>
        /// <returns>TBD</returns>
        public int IndexWhere(Func<byte, bool> p)
        {
            return Iterator().IndexWhere(p);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="elem">TBD</param>
        /// <returns>TBD</returns>
        public int IndexOf(byte elem)
        {
            return Iterator().IndexOf(elem);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public byte[] ToArray()
        {
            return Iterator().ToArray();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public abstract CompactByteString Compact();
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public abstract bool IsCompact();

        /// <summary>
        /// N/A
        /// </summary>
        /// <exception cref="NotSupportedException">
        /// This exception is thrown automatically since iterators aren't supported in <see cref="ByteString"/>.
        /// </exception>
        /// <returns>N/A</returns>
        public virtual IEnumerator<byte> GetEnumerator()
        {
            throw new NotSupportedException("Method iterator is not implemented in ByteString");
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// TBD
        /// </summary>
        public virtual bool IsEmpty
        {
            get { return Count == 0; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public bool NonEmpty
        {
            get { return !IsEmpty; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public abstract int Count { get; }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="that">TBD</param>
        /// <returns>TBD</returns>
        public abstract ByteString Concat(ByteString that);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public string DecodeString()
        {
            return DecodeString(Encoding.UTF8);
        }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="charset">TBD</param>
        /// <returns>TBD</returns>
        public abstract string DecodeString(Encoding charset);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="lhs">TBD</param>
        /// <param name="rhs">TBD</param>
        /// <returns>TBD</returns>
        public static ByteString operator +(ByteString lhs, ByteString rhs)
        {
            return lhs.Concat(rhs);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="buffer">TBD</param>
        /// <returns>TBD</returns>
        public int CopyToBuffer(ByteBuffer buffer)
        {
            return Iterator().CopyToBuffer(buffer);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="buffer">TBD</param>
        /// <returns>TBD</returns>
        public static ByteString Create(ByteBuffer buffer)
        {
            if (buffer.Remaining < 0) return Empty;
            var ar = new byte[buffer.Remaining];
            buffer.Get(ar);
            return new ByteString1C(ar);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="buffer">TBD</param>
        /// <param name="offset">TBD</param>
        /// <param name="length">TBD</param>
        /// <returns>TBD</returns>
        public static ByteString Create(byte[] buffer, int offset, int length)
        {
            if (length == 0) return Empty;
            var ar = new byte[length];
            Array.Copy(buffer, offset, ar, 0, length);
            return new ByteString1C(ar);
        }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="buffer">TBD</param>
        /// <returns>TBD</returns>
        public static ByteString Create(byte[] buffer)
        {
            return Create(buffer, 0, buffer.Length);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public bool Equals(ByteString other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;
            if (Count != other.Count) return false;
            for (int i = 0; i < Count; i++)
            {
                if (this[i] != other[i]) return false;
            }
            return true;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="obj">TBD</param>
        /// <returns>TBD</returns>
        public override bool Equals(object obj)
        {
            return obj is ByteString && Equals((ByteString) obj);
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    partial /*object*/ class CompactByteString
    {
        /// <summary>
        /// TBD
        /// </summary>
        internal static readonly CompactByteString EmptyCompactByteString = new ByteString1C(new byte[0]);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="str">TBD</param>
        /// <param name="encoding">TBD</param>
        /// <returns>TBD</returns>
        public static CompactByteString FromString(string str, Encoding encoding)
        {
            return string.IsNullOrEmpty(str)
                ? EmptyCompactByteString
                : new ByteString1C(encoding.GetBytes(str));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="array">TBD</param>
        /// <param name="offset">TBD</param>
        /// <param name="length">TBD</param>
        /// <returns>TBD</returns>
        public static ByteString FromArray(byte[] array, int offset, int length)
        {
            var copyOffset = Math.Max(offset, 0);
            var copyLength = Math.Max(Math.Min(array.Length - copyOffset, length), 0);
            if (copyLength == 0) return Empty;
            var copyArray = new byte[copyLength];
            Array.Copy(array, copyOffset, copyArray, 0, copyLength);
            return new ByteString1C(copyArray);
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    public abstract partial class CompactByteString : ByteString
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override bool IsCompact()
        {
            return true;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override CompactByteString Compact()
        {
            return this;
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class ByteStringBuilder
    {
        private readonly List<ByteString.ByteString1> _builder = new List<ByteString.ByteString1>();
        private int _length;
        private byte[] _temp;
        private int _tempLength;
        private int _tempCapacity;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="len">TBD</param>
        /// <returns>TBD</returns>
        protected Func<Action<byte[], int>, ByteStringBuilder> FillArray(int len)
        {
            return fill =>
            {
                EnsureTempSize(_tempLength + len);
                fill(_temp, _tempLength);
                _tempLength += len;
                _length += len;
                return this;
            };
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="len">TBD</param>
        /// <param name="byteOrder">TBD</param>
        /// <param name="fill">TBD</param>
        /// <returns>TBD</returns>
        protected ByteStringBuilder FillByteBuffer(int len, ByteOrder byteOrder, Action<ByteBuffer> fill)
        {
            return FillArray(len)((array, start) =>
            {
                var buffer = ByteBuffer.Wrap(array, start, len);
                buffer.Order(byteOrder);
                fill(buffer);
            });
        }

        /// <summary>
        /// TBD
        /// </summary>
        public int Length
        {
            get { return _length; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="len">TBD</param>
        /// <returns>TBD</returns>
        public void SizeHint(int len)
        {
            ResizeTemp(len - (_length - _tempLength));
        }

        private void ClearTemp()
        {
            if (_tempLength > 0)
            {
                var arr = new byte[_tempLength];
                Array.Copy(_temp, 0, arr, 0, _tempLength);
                _builder.Add(new ByteString.ByteString1(arr));
                _tempLength = 0;
            }
        }

        private void ResizeTemp(int size)
        {
            var newTemp = new byte[size];
            if (_tempLength > 0) Array.Copy(_temp, 0, newTemp, 0, _tempLength);
            _temp = newTemp;
            _tempCapacity = _temp.Length;
        }

        private void EnsureTempSize(int size)
        {
            if (_tempCapacity < size || _tempCapacity == 0)
            {
                var newSize = _tempCapacity == 0 ? 16 : _tempCapacity*2;
                while (newSize < size) newSize *= 2;
                ResizeTemp(newSize);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="xs">TBD</param>
        /// <returns>TBD</returns>
        public ByteStringBuilder Append(IEnumerable<byte> xs)
        {
            var bs1C = xs as ByteString.ByteString1C;
            if (bs1C != null)
            {
                ClearTemp();
                _builder.Add(bs1C.ToByteString1());
                _length += bs1C.Count;
                return this;
            }
            var bs1 = xs as ByteString.ByteString1;
            if (bs1 != null)
            {
                ClearTemp();
                _builder.Add(bs1);
                _length += bs1.Count;
                return this;
            }
            var bss = xs as ByteString.ByteStrings;
            if (bss != null)
            {
                ClearTemp();
                _builder.AddRange(bss.Items);
                _length += bss.Count;
                return this;
            }
            return xs.Aggregate(this, (a, x) => a.PutByte(x));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="x">TBD</param>
        /// <returns>TBD</returns>
        public ByteStringBuilder PutByte(byte x)
        {
            return this + x;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="x">TBD</param>
        /// <param name="byteOrder">TBD</param>
        /// <returns>TBD</returns>
        public ByteStringBuilder PutShort(int x, ByteOrder byteOrder)
        {
            if (byteOrder == ByteOrder.BigEndian)
            {
                PutByte((byte)(x >> 8));
                PutByte((byte)(x >> 0));
            }
            else
            {
                PutByte((byte)(x >> 0));
                PutByte((byte)(x >> 8));
            }
            return this;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="x">TBD</param>
        /// <param name="byteOrder">TBD</param>
        /// <returns>TBD</returns>
        public ByteStringBuilder PutInt(int x, ByteOrder byteOrder)
        {
            return FillArray(4)((target, offset) =>
            {
                if (byteOrder == ByteOrder.BigEndian)
                {
                    target[offset + 0] = (byte) (x >> 24);
                    target[offset + 1] = (byte) (x >> 16);
                    target[offset + 2] = (byte) (x >>  8);
                    target[offset + 3] = (byte) (x >>  0);
                }
                else
                {
                    target[offset + 0] = (byte)(x >>  0);
                    target[offset + 1] = (byte)(x >>  8);
                    target[offset + 2] = (byte)(x >> 16);
                    target[offset + 3] = (byte)(x >> 24);
                }
            });
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="array">TBD</param>
        /// <returns>TBD</returns>
        public ByteStringBuilder PutBytes(byte[] array)
        {
            return PutBytes(array, 0, array.Length);
        }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="array">TBD</param>
        /// <param name="start">TBD</param>
        /// <param name="len">TBD</param>
        /// <returns>TBD</returns>
        public ByteStringBuilder PutBytes(byte[] array, int start, int len)
        {
            return FillArray(len)((target, targetOffset) => Array.Copy(array, start, target, targetOffset, len));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="lhs">TBD</param>
        /// <param name="rhs">TBD</param>
        /// <returns>TBD</returns>
        public static ByteStringBuilder operator +(ByteStringBuilder lhs, byte rhs)
        {
            lhs.EnsureTempSize(lhs._tempLength + 1);
            lhs._temp[lhs._tempLength] = rhs;
            lhs._tempLength += 1;
            lhs._length += 1;
            return lhs;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public ByteString Result()
        {
            if (_length == 0) return ByteString.Empty;
            ClearTemp();
            var bytestrings = _builder;
            return bytestrings.Count == 1
                ? bytestrings[0] as ByteString
                : new ByteString.ByteStrings(bytestrings.ToArray());
        }
    }

    #region JVM

    /// <summary>
    /// TBD
    /// </summary>
    public enum ByteOrder
    {
        /// <summary>
        /// TBD
        /// </summary>
        BigEndian,
        /// <summary>
        /// TBD
        /// </summary>
        LittleEndian
    }

    #endregion
}