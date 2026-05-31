using System;
using System.Runtime.CompilerServices;
using MessagePack.Formatters;

namespace Akka.Serialization.MessagePack.Resolvers;

/// <summary>
/// A fast, Grow-only structure for holding Created formatters.
/// Uses Int indexes per-type to avoid ConcurrentDictionary bucket costs,
/// And avoids volatile/lock entirely on the happy path.
/// </summary>
/// <remarks>
/// This can be (in some cases) less space efficient than a normal dictionary,
/// However, for most internal uses, the cost is going to be fairly bounded,
/// and <see cref="PolymorphicFormatterResolver"/>'s behavior results in
/// <see cref="PolymorphicFormatterResolver.GetFormatter{T}"/> being a hot path,
/// So in general the alloc cost is worth it. 
/// </remarks>
internal sealed class IntIndexedMessagePackFormatterDict
{
    private IMessagePackFormatter?[] _formatters;
    private readonly object _lockObj = new();
    public IntIndexedMessagePackFormatterDict(int initialSize)
    {
        _formatters = new IMessagePackFormatter[256];
    }
        
    public bool TryGet(int i, out IMessagePackFormatter? formatter)
    {
        //No Fence here.
        //If the read happened to be dirty, that's fine.
        //Should it happen, it just means we trust `TryAdd`
        //which does a full fence and then checks state.
        //Shouldn't happen much and only on startup...
        var f = _formatters;
        if (i > f.Length)
        {
            formatter = default;
            return false;
        }
        else
        {
            formatter = f[i];
            return formatter != default;
        }
            
    }

    public IMessagePackFormatter TryAdd(int i, IMessagePackFormatter formatter)
    {
        //This may result in locks earlier on in an app,
        //However this will not impact types that can already be deserialized,
        //and since we are always in a full fence here can ensure we don't
        //accidentally fail to share an instance.
        if (i >= 0)
        {
            lock (_lockObj)
            {
                //We care about two things in the lock.
                //First, if we need a resize, we do the resize,
                //       and just copy the instance over because we have lock.
                //Also, Guard for edge case where we get a type that misses bounds,
                //      i.e. concurrent systems in same process.
                //Otherwise, we check whether we are there
                //           (if a competitor added it on resize, we see via fence)
                //           and if not, add ours.
                while (_formatters.Length <= i)
                {
                    //Resize. Copy everything over first,
                    //And then add our version before setting the new version.
                    var newF =
                        new IMessagePackFormatter?[_formatters.Length * 2];
                    _formatters.CopyTo(newF.AsSpan());
                    _formatters = newF;
                }

                if (_formatters[i] == null)
                {
                    _formatters[i] = formatter;
                }
            }

            return _formatters[i]!;
        }
        else
        {
            return ThrowOutOfRangeHelper(i, formatter);
        }
    }

    [MethodImplAttribute(MethodImplOptions.NoInlining)]
    private static IMessagePackFormatter ThrowOutOfRangeHelper(int index,
        IMessagePackFormatter formatter)
    {
        throw new IndexOutOfRangeException(
            $"Value must be non-negative! Value was {index}, For Formatter {formatter?.GetType().FullName ?? "null"}");
    }
}