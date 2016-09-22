//-----------------------------------------------------------------------
// <copyright file="ByteIterator.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Akka.IO;

namespace Akka.Util
{
    public abstract class ByteIterator
    {
        internal class ByteArrayIterator : ByteIterator
        {
            private byte[] _array;
            private int _until;
            private int _from;

            public ByteArrayIterator(byte[] array, int @from, int until)
            {
                _array = array;
                _from = @from;
                _until = until;
            }

            public override int Len
            {
                get { return _until - _from; }
            }

            public override bool HasNext
            {
                get { return _from < _until; }
            }

            public override byte Head
            {
                get { return _array[_from]; }
            }

            /// <summary></summary>
            /// <exception cref="IndexOutOfRangeException"></exception>
            public override byte Next()
            {
                if (!HasNext) throw new IndexOutOfRangeException();
                return _array[_from++];
            }

            protected override void Clear()
            {
                _array = new byte[0];
                _from = _until = 0;
            }

            public int Length()
            {
                var l = Len;
                Clear();
                return l;
            }

            public new ByteArrayIterator Clone()
            {
                return new ByteArrayIterator(_array, _from, _until);
            }

            public override ByteIterator Take(int n)
            {
                if (n < Len)
                    _until = n > 0 ? _from + n : _from;
                return this;
            }

            public override ByteIterator Drop(int n)
            {
                if (n > 0)
                    _from = n < Len ? _from + n : _until;
                return this;
            }

            public new ByteArrayIterator TakeWhile(Func<byte, bool> p)
            {
                var prev = _from;
                DropWhile(p);
                _until = _from;
                _from = prev;
                return this;
            }

            public new ByteArrayIterator DropWhile(Func<byte, bool> p)
            {
                var stop = false;
                while (!stop && HasNext)
                {
                    if (p(_array[_from]))
                        _from++;
                    else
                        stop = true;
                }
                return this;
            }

            public void CopToArray(byte[] xs, int start, int len)
            {
                var n = Math.Max(0, Math.Min(Math.Min(xs.Length - start, Len), len));
                Array.Copy(_array, _from, xs, start, n);
                Drop(n);
            }

            public override ByteString ToByteString()
            {
                var result = _from == 0 && _until == _array.Length
                    ? new ByteString.ByteString1C(_array) as ByteString
                    : new ByteString.ByteString1(_array, _from, Len);
                Clear();
                return result;
            }

            /// <summary></summary>
            /// <exception cref="IndexOutOfRangeException"></exception>
            public override ByteIterator GetBytes(byte[] xs, int offset, int n)
            {
                if (n > Len) throw new IndexOutOfRangeException();
                Array.Copy(_array, _from, xs, offset, n);
                return Drop(n);
            }
            
            public override byte[] ToArray()
            {
                var array = new byte[Len];
                CopToArray(array, 0, Len);
                return array;
            }

            public override int CopyToBuffer(ByteBuffer buffer)
            {
                var copyLength = Math.Min(buffer.Remaining, Len);
                if (copyLength > 0)
                {
                    buffer.Put(_array, _from, copyLength);
                    Drop(copyLength);
                }
                return copyLength;
            }
        }

        internal class MultiByteIterator : ByteIterator
        {
            private ILinearSeq<ByteArrayIterator> _iterators;
            private static readonly ILinearSeq<ByteArrayIterator> ClearedList = new ArrayLinearSeq<ByteArrayIterator>(new ByteArrayIterator[0]);

            public MultiByteIterator(params ByteArrayIterator[] iterators)
            {
                _iterators = new ArrayLinearSeq<ByteArrayIterator>(iterators);
                Normalize();
            }

            public MultiByteIterator(ILinearSeq<ByteArrayIterator> iterators)
            {
                _iterators = iterators;
                Normalize();
            }

            private MultiByteIterator Normalize()
            {
                Func<ILinearSeq<ByteArrayIterator>, ILinearSeq<ByteArrayIterator>> norm = null;
                norm = xs =>
                {
                    if (xs.IsEmpty) return ClearedList;
                    if (!xs.Head.HasNext) return norm(xs.Tail());
                    return xs;

                };
                _iterators = norm(_iterators);
                return this;
            }


            private ByteArrayIterator Current
            {
                get
                {
                    return _iterators.Head;
                }
            }
            private void DropCurrent()
            {
                _iterators = _iterators.Tail();
            }

            protected override void Clear()
            {
                _iterators = new ArrayLinearSeq<ByteArrayIterator>(new ByteArrayIterator[0]);
            }

            public override bool HasNext
            {
                get
                {
                    if (!_iterators.IsEmpty) return Current.HasNext;
                    return false;
                }
            }

            public override byte Head
            {
                get { return Current.Head; }
            }

            public override byte Next()
            {
                var result = Current.Next();
                Normalize();
                return result;
            }

            public override int Len
            {
                get { return _iterators.Aggregate(0, (a, x) => a + x.Len); }
            }


            public override ByteIterator Take(int n)
            {
                var rest = n;
                var builder = new List<ByteArrayIterator>();
                while (rest > 0 && !_iterators.IsEmpty)
                {
                    Current.Take(rest);
                    if (Current.HasNext)
                    {
                        rest -= Current.Len;
                        builder.Add(Current);
                    }
                    _iterators = _iterators.Tail();
                }
                _iterators = new ArrayLinearSeq<ByteArrayIterator>(builder.ToArray());
                return Normalize();
            }

            public override ByteIterator Drop(int n)
            {
                if (n > 0 && Len > 0)
                {
                    var nCurrent = Math.Min(n, Current.Len);
                    Current.Drop(n);
                    var rest = n - nCurrent;
                    Normalize();
                    return Drop(rest);
                }
                return this;
            }

            public override ByteIterator TakeWhile(Func<byte, bool> p)
            {
                var stop = false;
                var builder = new List<ByteArrayIterator>();
                while (!stop && !_iterators.IsEmpty)
                {
                    var lastLen = Current.Len;
                    Current.TakeWhile(p);
                    if (Current.HasNext) builder.Add(Current);
                    if (Current.Len < lastLen) stop = true;
                    DropCurrent();
                }
                _iterators = new ArrayLinearSeq<ByteArrayIterator>(builder.ToArray());
                return Normalize();
            }

            public override ByteIterator DropWhile(Func<byte, bool> p)
            {
                if (Len > 0)
                {
                    Current.DropWhile(p);
                    var dropMore = Current.Len == 0;
                    Normalize();
                    if (dropMore) return DropWhile(p);
                }
                return this;
            }

            public override ByteString ToByteString()
            {
                if (_iterators.Tail().IsEmpty) return _iterators.Head.ToByteString();
                var result = _iterators.Aggregate(ByteString.Empty, (a, x) => a + x.ToByteString());
                Clear();
                return result;
            }

            protected MultiByteIterator GetToArray<T>(T[] xs, int offset, int n, int elemSize, Func<T> getSingle, Action<T[], int, int> getMulti)
            {
                if(n <= 0) return this;
                Func<int> nDoneF = () =>
                {
                    if (Current.Len >= elemSize)
                    {
                        var nCurrent = Math.Min(n, Current.Len/elemSize);
                        getMulti(xs, offset, nCurrent);
                        return nCurrent;
                    }
                    else
                    {
                        xs[offset] = getSingle();
                        return 1;
                    }
                };
                var nDone = nDoneF();
                Normalize();
                return GetToArray(xs, offset + nDone, n - nDone, elemSize, getSingle, getMulti);
            }

            public override ByteIterator GetBytes(byte[] xs, int offset, int n)
            {
                return GetToArray(xs, offset, n, 1, GetByte, (a, b, c) => Current.GetBytes(a, b, c));
            }


            public override byte[] ToArray()
            {
                return GetBytes(Len);
            }

            public override int CopyToBuffer(ByteBuffer buffer)
            {
                var n = _iterators.Aggregate(0, (a, x) => a + x.CopyToBuffer(buffer));
                Normalize();
                return n;
            }
        }

        public abstract int Len { get; }
        public abstract bool HasNext { get; }
        public abstract byte Head { get; }
        public abstract byte Next();
        protected abstract void Clear();

        /// <summary>
        /// N/A
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        /// <returns>N/A</returns>
        public virtual ByteIterator Clone()
        {
            throw new NotSupportedException();
        }

        public Tuple<ByteIterator, ByteIterator> Duplicate()
        {
            return Tuple.Create(this, Clone());
        }

        public abstract ByteIterator Take(int n);
        public abstract ByteIterator Drop(int n);

        public virtual ByteIterator Slice(int @from, int until)
        {
            return @from > 0
                ? Drop(from).Take(until - @from)
                : Take(until);
        }

        /// <summary>
        /// N/A
        /// </summary>
        /// <param name="p">N/A</param>
        /// <exception cref="NotSupportedException"></exception>
        /// <returns>N/A</returns>
        public virtual ByteIterator TakeWhile(Func<byte, bool> p)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// N/A
        /// </summary>
        /// <param name="p">N/A</param>
        /// <exception cref="NotSupportedException"></exception>
        /// <returns>N/A</returns>
        public virtual ByteIterator DropWhile(Func<byte, bool> p)
        {
            throw new NotSupportedException();
        }

        public virtual Tuple<ByteIterator, ByteIterator> Span(Func<byte, bool> p)
        {
            var that = Clone();
            TakeWhile(p);
            that.Drop(Len);
            return Tuple.Create(this, that);
        }

        public virtual int IndexWhere(Func<byte, bool> p)
        {
            var index = 0;
            var found = false;
            while (!found && HasNext)
                if (p(Next())) found = true;
                else index += 1;
            return found ? index : -1;
        }

        public virtual int IndexOf(byte elem)
        {
            return IndexWhere(x => x == elem);
        }

        public abstract ByteString ToByteString();

        public virtual void ForEach(Action<byte> f)
        {
            while (HasNext) f(Next());
        }

        public virtual T FoldLeft<T>(T z, Func<T, Byte, T> op)
        {
            var acc = z;
            ForEach(x => acc = op(acc, x));
            return acc;
        }

        public abstract byte[] ToArray();

        /// <summary>Get a single Byte from this iterator. Identical to next().</summary>
        public virtual byte GetByte()
        {
            return Next();
        }

        /// <summary>Get a single Short from this iterator.</summary>
        public short GetShort(ByteOrder byteOrder = ByteOrder.BigEndian)
        {
            return byteOrder == ByteOrder.BigEndian
                ? (short) (((Next() & 0xff) << 8) | ((Next() & 0xff) << 0))
                : (short) (((Next() & 0xff) << 0) | ((Next() & 0xff) << 8));
        }

        /// <summary>Get a single Int from this iterator.</summary>
        public int GetInt(ByteOrder byteOrder = ByteOrder.BigEndian)
        {
            return byteOrder == ByteOrder.BigEndian
                         ? (((Next() & 0xff) << 24)
                          | ((Next() & 0xff) << 16)
                          | ((Next() & 0xff) << 8)
                          | ((Next() & 0xff) << 0))
                         : (((Next() & 0xff) << 0)
                          | ((Next() & 0xff) << 8)
                          | ((Next() & 0xff) << 16)
                          | ((Next() & 0xff) << 24));
        }

        /// <summary>Get a single Long from this iterator.</summary>
        public long GetLong(ByteOrder byteOrder = ByteOrder.BigEndian)
        {
            return byteOrder == ByteOrder.BigEndian
                ? (short) (((long) (Next() & 0xff) << 56)
                                | ((Next() & 0xff) << 48)
                                | ((Next() & 0xff) << 40)
                                | ((Next() & 0xff) << 32)
                                | ((Next() & 0xff) << 24)
                                | ((Next() & 0xff) << 16)
                                | ((Next() & 0xff) <<  8)
                                | ((Next() & 0xff) <<  0))
                : (short) (((long) (Next() & 0xff) <<  0)
                                | ((Next() & 0xff) <<  8)
                                | ((Next() & 0xff) << 16)
                                | ((Next() & 0xff) << 24)
                                | ((Next() & 0xff) << 32)
                                | ((Next() & 0xff) << 40)
                                | ((Next() & 0xff) << 48)
                                | ((Next() & 0xff) << 56));
        }

        public abstract ByteIterator GetBytes(byte[] xs, int offset, int n);

        public byte[] GetBytes(int n)
        {
            var bytes = new byte[n];
            GetBytes(bytes, 0, n);
            return bytes;
        }

      public abstract int CopyToBuffer(ByteBuffer buffer);
    }



    public interface ILinearSeq<out T> : IEnumerable<T>
    {
        bool IsEmpty { get; }
        T Head { get; }
        ILinearSeq<T> Tail();
    }

    public class ArrayLinearSeq<T> : ILinearSeq<T>
    {
        private readonly T[] _array;
        private readonly int _offset;
        private readonly int _length;

        public ArrayLinearSeq(T[] array) : this(array, 0, array.Length)
        {
        }

        private ArrayLinearSeq(T[] array, int offset, int length)
        {
            _array = array;
            _offset = offset;
            _length = length;
        }

        public bool IsEmpty
        {
            get { return _length == 0; }
        }

        public T Head
        {
            get { return _array[_offset]; }
        }

        public ILinearSeq<T> Tail()
        {
            return new ArrayLinearSeq<T>(_array, _offset + 1, _length - 1);
        }

        public IEnumerator<T> GetEnumerator()
        {
            return new Enumerator(this);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public static implicit operator ArrayLinearSeq<T>(T[] that)
        {
            return new ArrayLinearSeq<T>(that);
        }

        private class Enumerator : IEnumerator<T>
        {
            private readonly ILinearSeq<T> _orig;
            private ILinearSeq<T> _seq;
            private T _current;

            public Enumerator(ILinearSeq<T> seq)
            {
                _seq = seq;
                _orig = _seq;
            }

            public void Dispose()
            {

            }

            public bool MoveNext()
            {
                if (_seq.IsEmpty)
                    return false;
                _current = _seq.Head;
                _seq = _seq.Tail();
                return true;
            }

            public void Reset()
            {
                _seq = _orig;
            }

            public T Current
            {
                get { return _current; }
            }

            object IEnumerator.Current
            {
                get { return Current; }
            }

        }
    }
}