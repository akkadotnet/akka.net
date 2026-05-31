using System;
using System.Collections.Concurrent;
using Akka.Actor;
using MessagePack;
using MessagePack.Formatters;

namespace Akka.Serialization.MessagePack.Resolvers
{
    public class BackwardsCompatibleSurrogatedFormatterResolver : IFormatterResolver, IDoNotUsePolymorphicFormatter
    {

        private readonly ConcurrentDictionary<Type, IMessagePackFormatter>
            _formatterCache =
                new ConcurrentDictionary<Type, IMessagePackFormatter>();

        private readonly Func<Type, IMessagePackFormatter> _formatterCreateFunc;
        public BackwardsCompatibleSurrogatedFormatterResolver(ExtendedActorSystem system)
        {
            //Cast in the func since we'll have to cache anyway.
            //The alternative is making a 'nullable' func in another static class,
            //But that may result in too much garbage for other types.
            _formatterCreateFunc = t =>
                (IMessagePackFormatter)typeof(BackwardsCompatibleSurrogatedFormatter<>)
                    .MakeGenericType(t)
                    .GetConstructor(new[] { typeof(ActorSystem) })
                    .Invoke(new[] { system });

        }
        public IMessagePackFormatter<T> GetFormatter<T>()
        {
            if (SurrogateResolvable<T>.IsSurrogated)
            {
                return (IMessagePackFormatter<T>)_formatterCache.GetOrAdd(
                    typeof(T),
                    _formatterCreateFunc);
            }
            else
            {
                return null;
            }
        }
        
    }
}