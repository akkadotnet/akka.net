using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Akka.Remote.Artery.Internal
{
    internal class ByteBuffer : IDisposable
    {
        private readonly byte[] _buffer;
        private readonly MemoryStream _stream;
        private readonly BinaryWriter _writer;
        private readonly BinaryReader _reader;

        public long Position
        {
            get => _stream.Position;
            set => _stream.Position = value;
        }

        public long Capacity => _buffer.Length;

        public long RemainingCapacity => _buffer.Length - Position;


        public ByteBuffer(int capacity): this(new byte[capacity])
        { }

        public ByteBuffer(byte[] buffer)
        {
            _buffer = buffer;
            _stream = new MemoryStream(_buffer);
            _writer = new BinaryWriter(_stream, Encoding.UTF8);
            _reader = new BinaryReader(_stream, Encoding.UTF8);
        }

        public byte[] GetBuffer() => _buffer;

        public void Clear()
        {
            Position = 0;
            _buffer.AsSpan().Fill(0);
        }

        #region Put ops
        public void Put(byte[] bytes, int offset, int length)
            => _writer.Write(bytes, offset, length);

        public void Put(byte b) => _writer.Write(b);

        public void Put(sbyte b) => _writer.Write(b);

        public void Put(long position, byte b)
        {
            Position = position;
            _writer.Write(b);
        }

        public void Put(long position, sbyte b)
        {
            _stream.Position = position;
            _writer.Write(b);
        }

        public void PutShort(short s) => _writer.Write(s);

        public void PutShort(long position, short s)
        {
            _stream.Position = position;
            _writer.Write(s);
        }

        public void PutShort(ushort s) => _writer.Write(s);

        public void PutShort(long position, ushort s)
        {
            _stream.Position = position;
            _writer.Write(s);
        }

        public void PutInt(int i) => _writer.Write(i);

        public void PutInt(long position, int i)
        {
            _stream.Position = position;
            _writer.Write(i);
        }

        public void PutLong(long l) => _writer.Write(l);

        public void PutLong(long position, long l)
        {
            _stream.Position = position;
            _writer.Write(l);
        }
        #endregion

        #region Get ops
        public void Get(ref byte[] bytes, int offset, int length)
            => _reader.Read(bytes, offset, length);

        public byte Get()
            => _reader.ReadByte();

        public byte Get(long position)
        {
            _stream.Position = position;
            return _reader.ReadByte();
        }

        public byte[] GetBytes(int length)
            => _reader.ReadBytes(length);

        public byte[] GetBytes(long position, int length)
        {
            _stream.Position = position;
            return _reader.ReadBytes(length);
        }

        public short GetShort()
            => _reader.ReadInt16();

        public short GetShort(long position)
        {
            _stream.Position = position;
            return _reader.ReadInt16();
        }

        public ushort GetUShort()
            => _reader.ReadUInt16();

        public ushort GetUShort(long position)
        {
            _stream.Position = position;
            return _reader.ReadUInt16();
        }

        public int GetInt()
            => _reader.ReadInt32();

        public int GetInt(long position)
        {
            _stream.Position = position;
            return _reader.ReadInt32();
        }

        public long GetLong()
            => _reader.ReadInt64();

        public long GetLong(long position)
        {
            _stream.Position = position;
            return _reader.ReadInt64();
        }
        #endregion

        public void Dispose()
        {
            _stream?.Dispose();
            _writer?.Dispose();
            _reader?.Dispose();
        }

        public ByteBuffer Clone()
        {
            var bytes = new byte[_buffer.Length];
            Array.Copy(_buffer, bytes, _buffer.Length);
            var newByteBuffer = new ByteBuffer(bytes);
            newByteBuffer.Position = Position;
            return newByteBuffer;
        }
    }
}
