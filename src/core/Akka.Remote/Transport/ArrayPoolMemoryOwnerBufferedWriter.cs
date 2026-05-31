using System;
using System.Buffers;

namespace Akka.Remote.Transport;

public class ReservedBufferRangeSegment
{
    private readonly IReservableSegmentBufferWriter _bufferWriter;
    public readonly int Offset;
    public readonly int Length;
    
    public ReservedBufferRangeSegment(IReservableSegmentBufferWriter bufferWriter, int offset, int length)
    {
        _bufferWriter = bufferWriter;
        Offset = offset;
        Length = length;
    }
    public Memory<byte> GetMemory() => _bufferWriter.GetReservedSegment(Offset, Length);
}
public interface IReservableSegmentBufferWriter : IBufferWriter<byte>
{
    /// <summary>
    /// Reserves a segment of the buffer for writing. The returned <see cref="ReservedBufferRangeSegment"/>
    /// contains the offset and length of the reserved segment. The caller can then write directly to the
    /// underlying buffer at the specified offset and length, ADVANCE
    /// the number of bytes actually written.
    /// </summary>
    ReservedBufferRangeSegment Reserve(int sizeHint);
    
    Memory<byte> GetReservedSegment(int offset, int length);

    ReadOnlyMemory<byte> GetFullSegment();
}
public sealed class ReservableSegmentArrayPooledMemoryOwnerBufferWriter : IReservableSegmentBufferWriter
{
    private readonly ArrayPool<byte> _pool;
    private byte[] _array;
    private int _position;
    
    public ReservableSegmentArrayPooledMemoryOwnerBufferWriter(int initialSize = 256) 
        : this(ArrayPool<byte>.Shared, initialSize) { }
    public ReservableSegmentArrayPooledMemoryOwnerBufferWriter(ArrayPool<byte> pool, int initialSize = 256)
    {
        _pool = pool;
        _array = _pool.Rent(256); // Start with a reasonable default size
    }

    public ReadOnlyMemory<byte> GetFullSegment() => new(_array, 0, _array.Length - _position);
    public void Advance(int count)
    {
        _position += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        if (_position + sizeHint > _array.Length)
        {
            Grow(sizeHint);
        }
        return new(_array, _position, _array.Length - _position);
    }

    private void Grow(int sizeHint)
    {
        var newSize = Math.Max(_position + sizeHint, _array.Length * 2);
        var newArray = _pool.Rent(newSize);
        Array.Copy(_array, newArray, _position);
        _pool.Return(_array);
        _array = newArray;
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        return GetMemory(sizeHint).Span;
    }

    public void Dispose()
    {
        if (_array is not null)
            _pool.Return(_array);
    }

    public ReservedBufferRangeSegment Reserve(int sizeHint)
    {
        GetMemory(sizeHint);
        var offset = _position;
        Advance(sizeHint);
        return new ReservedBufferRangeSegment(this, offset, sizeHint);
    }

    public Memory<byte> GetReservedSegment(int offset, int length)
    {
        return new(_array, offset, length);
    }
}
public class ArrayPoolMemoryOwnerBufferedWriter
{
    public static ArrayPoolMemoryOwnerBufferedWriter<T> Create<T>()
    {
        return new ArrayPoolMemoryOwnerBufferedWriter<T>(ArrayPool<T>.Shared);
    }
}


/// <summary>
/// Provides an implementation of <see cref="IMemoryOwner{T}"/> and <see cref="IBufferWriter{T}"/>
/// that uses an <see cref="ArrayPool{T}"/> to rent and return arrays as needed.
/// Use the IBufferWriter{T} interface to write to the array.
/// Use the IMemoryOwner{T} interface to get a read-only view of the array.
/// Properly disposing avoids future allocations.
/// Not disposing will not leak memory, thanks to ArrayPool Semantics.
/// </summary>
/// <typeparam name="T"></typeparam>
public sealed class ArrayPoolMemoryOwnerBufferedWriter<T> : IMemoryOwner<T>, IBufferWriter<T>
{
    private readonly ArrayPool<T> _pool;
    private T[] _array;
    private int _position;
    
    public ArrayPoolMemoryOwnerBufferedWriter(ArrayPool<T> pool, int initialSize = 256)
    {
        _pool = pool;
        _array = _pool.Rent(256); // Start with a reasonable default size
    }

    public Memory<T> Memory => new(_array, 0, _array.Length - _position);
    public void Advance(int count)
    {
        _position += count;
    }

    public Memory<T> GetMemory(int sizeHint = 0)
    {
        if (_position + sizeHint > _array.Length)
        {
            Grow(sizeHint);
        }
        return new(_array, _position, _array.Length - _position);
    }

    private void Grow(int sizeHint)
    {
        var newSize = Math.Max(_position + sizeHint, _array.Length * 2);
        var newArray = _pool.Rent(newSize);
        Array.Copy(_array, newArray, _position);
        _pool.Return(_array);
        _array = newArray;
    }

    public Span<T> GetSpan(int sizeHint = 0)
    {
        return GetMemory(sizeHint).Span;
    }

    public void Dispose()
    {
        if (_array is not null)
            _pool.Return(_array);
    }
}