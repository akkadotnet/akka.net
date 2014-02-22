using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Pigeon.Dispatch;

namespace Pigeon.Actor
{
    public interface ICanTell
    {
        void Tell(object mssage, ActorRef sender);
    }

    /// <summary>
    /// Extension method class designed to create Ask support for
    /// non-ActorRef objects such as <see cref="ActorSelection"/>.
    /// </summary>
    public static class Futures
    {
        //when asking from outside of an actor, we need to pass a system, so the FutureActor can register itself there and be resolvable for local and remote calls
        public static Task<object> Ask(this ICanTell self, object message, ActorSystem system)
        {
            if (ActorCell.Current != null)
                throw new NotSupportedException("Do not use this method to Ask from within an actor, use Ask(self,message) instead");

            return Ask(self, null ,message, system.Provider);
        }

        //when asking from within an actor, we already know what system the FutureActor should be long to, so we use the active context
        public static Task<object> Ask(this ICanTell self, object message)
        {
            if (ActorCell.Current == null)
                throw new NotSupportedException("This method may only be used when Asking from within an actor, use Ask(self,message,system) instead");

            //by passing a replyTo arg (Self) we make sure that the TaskCompletionSource will get resolved in the actors mailbox and thus not break actor concurrency
            return Ask(self, ActorCell.Current.Self, message, ActorCell.Current.System.Provider);
        }

        private static Task<object> Ask(ICanTell self, ActorRef replyTo, object message, ActorRefProvider provider)
        {
            var result = new TaskCompletionSource<object>();
            //create a new tempcontainer path
            var path = provider.TempPath();
            //callback to unregister from tempcontainer
            Action unregister = () => provider.UnregisterTempActor(path);
            FutureActorRef future = new FutureActorRef(result, replyTo, unregister, path);
            //The future actor needs to be registered in the temp container
            provider.RegisterTempActor(future, path);
            self.Tell(message, future);
            return result.Task;
        }
    }
}
