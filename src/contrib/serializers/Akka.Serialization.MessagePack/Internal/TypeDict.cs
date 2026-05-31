namespace Akka.Serialization.MessagePack.Resolvers;

/// <summary>
/// Used to provide indexes into a fast array lookup for resolvers.
/// </summary>
internal static class TypeDict<T>
{
    public static readonly int TypeVal = TypeDictCtr.doNotCallExternally();
}