using System;
using System.Linq;
using System.Reactive.Streams;

namespace Akka.Streams.Util
{
    public static class TypeExtensions
    {
        public static Type GetSubscribedType(this Type type)
        {
            return
                type
                    .GetInterfaces()
                    .Single(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof (ISubscriber<>))
                    .GetGenericArguments()
                    .First();
        }

        public static Type GetPublishedType(this Type type)
        {
            return
                type
                    .GetInterfaces()
                    .Single(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof (IPublisher<>))
                    .GetGenericArguments()
                    .First();
        }
    }
}