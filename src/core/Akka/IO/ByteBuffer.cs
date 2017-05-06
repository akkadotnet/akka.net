//-----------------------------------------------------------------------
// <copyright file="ByteBuffer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
using System;

namespace Akka.IO
{
    /// <summary>
    /// TBD
    /// </summary>
    public class ByteBuffer
    {
        private byte[] _array;
        private int _limit;
        private int _position;
        private ByteOrder _order;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="array">TBD</param>
        public ByteBuffer(byte[] array)
            : this(array, 0, array.Length)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="array">TBD</param>
        /// <param name="offset">TBD</param>
        /// <param name="length">TBD</param>
        public ByteBuffer(byte[] array, int offset, int length)
        {
            _array = array;
            _position = offset;
            _limit = offset + length;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public void Clear()
        {
            _position = 0;
            _limit = _array.Length;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="maxBufferSize">TBD</param>
        public void Limit(int maxBufferSize)
        {
            _array = new byte[maxBufferSize];
            _limit = maxBufferSize;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public void Flip()
        {
            _limit = _position;
            _position = 0;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public bool HasRemaining
        {
            get { return _position < _limit; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public int Remaining
        {
            get { return _limit - _position; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public byte[] Array()
        {
            return _array;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="src">TBD</param>
        public void Put(byte[] src)
        {
            var len = Math.Min(src.Length, Remaining);
            System.Array.Copy(src, 0, _array, _position, len);
            _position += len;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="array">TBD</param>
        /// <param name="start">TBD</param>
        /// <param name="len">TBD</param>
        /// <returns>TBD</returns>
        public static ByteBuffer Wrap(byte[] array, int start, int len)
        {
            return new ByteBuffer(array, start, len);
        }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="array">TBD</param>
        /// <returns>TBD</returns>
        public static ByteBuffer Wrap(byte[] array)
        {
            return new ByteBuffer(array, 0, array.Length);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="capacity">TBD</param>
        /// <returns>TBD</returns>
        public static ByteBuffer Allocate(int capacity)
        {
            return new ByteBuffer(new byte[capacity]);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="byteOrder">TBD</param>
        public void Order(ByteOrder byteOrder)
        {
            _order = byteOrder;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="array">TBD</param>
        /// <param name="from">TBD</param>
        /// <param name="copyLength">TBD</param>
        public void Put(byte[] array, int @from, int copyLength)
        {
            System.Array.Copy(array, @from, _array, _position, copyLength);
            _position += copyLength;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="ar">TBD</param>
        /// <param name="offset">TBD</param>
        /// <param name="length">TBD</param>
        public void Get(byte[] ar, int offset, int length)
        {
            if (length > Remaining)
                throw new IndexOutOfRangeException();
            System.Array.Copy(_array, _position, ar, offset, length);
            _position += length;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="ar">TBD</param>
        public void Get(byte[] ar)
        {
            Get(ar, 0, ar.Length);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="src">TBD</param>
        /// <param name="length">TBD</param>
        public void Put(ByteBuffer src, int length)
        {
            Put(src._array, src._position, length);
            src._position += length;
        }
    }
}
