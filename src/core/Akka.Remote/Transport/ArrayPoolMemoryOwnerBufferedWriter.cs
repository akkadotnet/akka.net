using System;
using System.Buffers;

namespace Akka.Remote.Transport;

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