using System;
using Akka.Actor;
// ReSharper disable once CheckNamespace


namespace Akka.TestKit
{
    public static class AskExtensions
    {
        public static TAnswer AskAndWait<TAnswer>(this ICanTell self, object message, TimeSpan timeout)
        {
            var task = self.Ask<TAnswer>(message,timeout);
            task.Wait();
            return task.Result;
        }
    }
}