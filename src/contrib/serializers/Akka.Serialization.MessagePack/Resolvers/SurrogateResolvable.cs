using Akka.Util;

namespace Akka.Serialization.MessagePack.Resolvers
{
    static class SurrogateResolvable<T>
    {
        public static readonly bool IsSurrogated = (typeof(ISurrogated).IsAssignableFrom(typeof(T)));
        public static readonly bool IsSurrogate =
            typeof(ISurrogate).IsAssignableFrom(typeof(T));
    }
}