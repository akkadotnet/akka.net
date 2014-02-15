using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Pigeon.Actor
{
    class PreRestartException : Exception
    {
        private LocalActorRef Actor;
        private Exception e;
        private Exception exception;
        private object optionalMessage;

        public PreRestartException(LocalActorRef actor, Exception restartException, Exception cause, object optionalMessage)
        {
            // TODO: Complete member initialization
            this.Actor = actor;
            this.e = restartException;
            this.exception = cause;
            this.optionalMessage = optionalMessage;
        }
    }
}
