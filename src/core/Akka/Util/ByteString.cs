//-----------------------------------------------------------------------
// <copyright file="ByteString.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
        public ByteString FromArray(byte[] array)
        {
            return new ByteString1C((byte[]) array.Clone());
        }

        /// <summary>
        /// Creates a new ByteString by copying length bytes starting at offset from
        /// an Array.
        /// </summary>
        public ByteString FromArray(byte[] array, int offset, int length)
        {
            return CompactByteString.FromArray(array, offset, length);
        }

        /// <summary>
        /// Creates a new ByteString which will contain the UTF-8 representation of the given String
        /// </summary>
        public static ByteString FromString(string str)
        {
            return FromString(str, Encoding.UTF8);
        }

        /// <summary>
        /// Creates a new ByteString which will contain the representation of the given String in the given charset
        /// </summary>
        public static ByteString FromString(string str, Encoding encoding)
        {
            return CompactByteString.FromString(str, encoding);
        }

        public static ByteString FromByteBuffer(ByteBuffer buffer)
        {
            return Create(buffer);
        }

        public static readonly ByteString Empty = CompactByteString.EmptyCompactByteString;

        public static ByteStringBuilder NewBuilder()
        {
            return new ByteStringBuilder();
        }

        internal class ByteString1C : CompactByteString
        {
            private readonly byte[] _bytes;

            public ByteString1C(byte[] bytes)
            {
                _bytes = bytes;
            }

            public override byte this[int idx]
            {
                get { return _bytes[idx]; }
            }

            public override int Count
            {
                get { return _bytes.Length; }
            }

            public override ByteIterator Iterator()
            {
                return new ByteIterator.ByteArrayIterator(_bytes, 0, _bytes.Length);
            }

            public override IEnumerator<byte> GetEnumerator()
            {
                return ((IEnumerable<byte>) _bytes).GetEnumerator();
            }

            internal ByteString1 ToByteString1()
            {
                return new ByteString1(_bytes);
            }

            public override ByteString Concat(ByteString that)
            {
                if (that.IsEmpty) return this;
                if (this.IsEmpty) return that;
                return ToByteString1() + that;
            }

            public override ByteString Slice(int from, int until)
            {
                return (from != 0 || until != Count)
                    ? ToByteString1().Slice(from, until)
                    : this;
            }

            public override string DecodeString(Encoding charset)
            {
                return IsEmpty ? string.Empty : charset.GetString(_bytes);

            }
        }

        internal class ByteString1 : ByteString
        {
            private readonly byte[] _bytes;
            private readonly int _startIndex;
            private readonly int _length;

            public ByteString1(byte[] bytes, int startIndex, int length)
            {
                _bytes = bytes;
                _startIndex = startIndex;
                _length = length;
            }

            public ByteString1(byte[] bytes)
                : this(bytes, 0, bytes.Length)
            {
            }

            public override byte this[int idx]
            {
                get { return _bytes[checkRangeConvert(idx)]; }
            }

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

            public override bool IsCompact()
            {
                return _length == _bytes.Length;
            }

            public override CompactByteString Compact()
            {
                return IsCompact()
                    ? new ByteString1C(_bytes)
                    : new ByteString1C(ToArray());
            }

            public override int Count
            {
                get { return _length; }
            }

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

            public override string DecodeString(Encoding charset)
            {
                return charset.GetString(_length == _bytes.Length ? _bytes : ToArray());
            }

            public override IEnumerator<byte> GetEnumerator()
            {
                return _bytes.Skip(_startIndex).Take(_length).GetEnumerator();
            }
        }

        internal class ByteStrings : ByteString
        {
            private readonly ByteString1[] _byteStrings;
            private readonly int _length;

            public ByteStrings(params ByteString1[] byteStrings)
                : this(byteStrings, byteStrings.Sum(x => x.Count))
            {
            }

            public ByteStrings(ByteString1[] byteStrings, int length)
            {
                _byteStrings = byteStrings;
                _length = length;
            }

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

            public override ByteIterator Iterator()
            {
                return new ByteIterator.MultiByteIterator(
                        _byteStrings.Select(x => (ByteIterator.ByteArrayIterator) x.Iterator()).ToArray());
            }

			public override IEnumerator<byte> GetEnumerator()
			{
				return _byteStrings.SelectMany(byteString => byteString).GetEnumerator();
			}

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

                    throw new InvalidOperationException("No suitable implementation found for concatenating ByteString of type " + that.GetType());
                }
            }

            public override bool IsCompact()
            {
                return _byteStrings.Length == 1 && _byteStrings.Head().IsCompact();
            }

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

            public override int Count
            {
                get { return _length; }
            }

            internal ByteString1[] Items
            {
                get { return _byteStrings; }
            }

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
    public abstract partial class ByteString : IReadOnlyList<byte>
    {
        public abstract byte this[int index] { get; }

        protected virtual ByteStringBuilder newBuilder()
        {
            return NewBuilder();
        }


        public abstract ByteIterator Iterator();

        public Byte Head
        {
            get { return this[0]; }
        }
        public ByteString Tail()
        {
            return Drop(1);
        }
        public byte Last
        {
            get { return this[Count - 1]; }
        }
        public ByteString Init()
        {
            return DropRight(1);
        }

        public virtual ByteString Slice(int @from, int until)
        {
            if (@from == 0 && until == Count) return this;
            return Iterator().Slice(@from, until).ToByteString();
        }

        public ByteString Take(int n)
        {
            return Slice(0, n);
        }
        public ByteString TakeRight(int n)
        {
            return Slice(Count - n, Count);
        }
        public ByteString Drop(int n)
        {
            return Slice(n, Count);
        }
        public ByteString DropRight(int n)
        {
            return Slice(0, Count - n);
        }

        public ByteString TakeWhile(Func<byte, bool> p)
        {
            return Iterator().TakeWhile(p).ToByteString();
        }

        public ByteString DropWhile(Func<byte, bool> p)
        {
            return Iterator().DropWhile(p).ToByteString();
        }

        public Tuple<ByteString, ByteString> Span(Func<byte, bool> p)
        {
            var span = Iterator().Span(p);
            return Tuple.Create(span.Item1.ToByteString(), span.Item2.ToByteString());
        }

        public Tuple<ByteString, ByteString> SplitAt(int n)
        {
            return Tuple.Create(Take(n), Drop(n));
        }

        public int IndexWhere(Func<byte, bool> p)
        {
            return Iterator().IndexWhere(p);
        }

        public int IndexOf(byte elem)
        {
            return Iterator().IndexOf(elem);
        }

        public byte[] ToArray()
        {
            return Iterator().ToArray();
        }

        public abstract CompactByteString Compact();
        public abstract bool IsCompact();

        public virtual IEnumerator<byte> GetEnumerator()
        {
            throw new NotSupportedException("Method iterator is not implemented in ByteString");
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public virtual bool IsEmpty
        {
            get { return Count == 0; }
        }

        public bool NonEmpty
        {
            get { return !IsEmpty; }
        }

        public abstract int Count { get; }
        public abstract ByteString Concat(ByteString that);

        public string DecodeString()
        {
            return DecodeString(Encoding.UTF8);
        }
        public abstract string DecodeString(Encoding charset);

        public static ByteString operator +(ByteString lhs, ByteString rhs)
        {
            return lhs.Concat(rhs);
        }

        public int CopyToBuffer(ByteBuffer buffer)
        {
            return Iterator().CopyToBuffer(buffer);
        }

        public static ByteString Create(ByteBuffer buffer)
        {
            if (buffer.Remaining < 0) return Empty;
            var ar = new byte[buffer.Remaining];
            buffer.Get(ar);
            return new ByteString1C(ar);
        }

        public static ByteString Create(byte[] buffer, int offset, int length)
        {
            if (length == 0) return Empty;
            var ar = new byte[length];
            Array.Copy(buffer, offset, ar, 0, length);
            return new ByteString1C(ar);
        }
        public static ByteString Create(byte[] buffer)
        {
            return Create(buffer, 0, buffer.Length);
        }
    }

    partial /*object*/ class CompactByteString
    {
        internal static readonly CompactByteString EmptyCompactByteString = new ByteString1C(new byte[0]);

        public static CompactByteString FromString(string str, Encoding encoding)
        {
            return string.IsNullOrEmpty(str)
                ? EmptyCompactByteString
                : new ByteString1C(encoding.GetBytes(str));
        }

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
#if !DNXCORE50
    [Serializable]
#endif
    public abstract partial class CompactByteString : ByteString
    {
        public override bool IsCompact()
        {
            return true;
        }

        public override CompactByteString Compact()
        {
            return this;
        }
    }

    public class ByteStringBuilder
    {
        private readonly List<ByteString.ByteString1> _builder = new List<ByteString.ByteString1>();
        private int _length;
        private byte[] _temp;
        private int _tempLength;
        private int _tempCapacity;

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

        protected ByteStringBuilder FillByteBuffer(int len, ByteOrder byteOrder, Action<ByteBuffer> fill)
        {
            return FillArray(len)((array, start) =>
            {
                var buffer = ByteBuffer.Wrap(array, start, len);
                buffer.Order(byteOrder);
                fill(buffer);
            });
        }

        public int Length
        {
            get { return _length; }
        }

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

        public ByteStringBuilder PutByte(byte x)
        {
            return this + x;
        }

        public ByteStringBuilder PutShort(int x, ByteOrder byteOrder)
        {
            if (byteOrder == ByteOrder.BigEndian)
            {
                PutByte(Convert.ToByte(x >> 8));
                PutByte(Convert.ToByte(x >> 0));
            }
            else
            {
                PutByte(Convert.ToByte(x >> 0));
                PutByte(Convert.ToByte(x >> 8));
            }
            return this;
        }

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

        public ByteStringBuilder PutBytes(byte[] array)
        {
            return PutBytes(array, 0, array.Length);
        }
        public ByteStringBuilder PutBytes(byte[] array, int start, int len)
        {
            return FillArray(len)((target, targetOffset) => Array.Copy(array, start, target, targetOffset, len));
        }

        public static ByteStringBuilder operator +(ByteStringBuilder lhs, byte rhs)
        {
            lhs.EnsureTempSize(lhs._tempLength + 1);
            lhs._temp[lhs._tempLength] = rhs;
            lhs._tempLength += 1;
            lhs._length += 1;
            return lhs;
        }

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

    public enum ByteOrder
    {
        BigEndian,
        LittleEndian
    }

    #endregion
}