using System.Threading;

namespace Akka.Serialization.MessagePack.Resolvers;

/// <summary>
/// Used as a Counter for <see cref="TypeDict{T}"/> calls.
/// </summary>
internal static class TypeDictCtr
{
    private static int _typeCtr = -1;

    internal static int doNotCallExternally()
    {
        return Interlocked.Increment(ref _typeCtr);
    }
}