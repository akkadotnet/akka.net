using System;
using System.Collections.Generic;
using System.Text;
using Akka.Actor;

namespace Akka.Pattern
{
    public class UserCalledFailException : AkkaException
    {
        public UserCalledFailException() : base($"User code caused [{nameof(CircuitBreaker)}] to fail because it calls the [{nameof(CircuitBreaker.Fail)}()] method.")
        { }
    }
}
