//-----------------------------------------------------------------------
// <copyright file="SubscriptionWithCancelException.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Annotations;
using Reactive.Streams;

namespace Akka.Streams
{
    public static class SubscriptionWithCancelException
    {
        [DoNotInherit]
        public abstract class NonFailureCancellation : Exception
        {
            public sealed override string StackTrace => "";
        }
        
        public sealed class NoMoreElementsNeeded : NonFailureCancellation
        {
            public static readonly NoMoreElementsNeeded Instance = new NoMoreElementsNeeded();
            private NoMoreElementsNeeded() { }
        }
    
        public sealed class StageWasCompleted : NonFailureCancellation
        {
            public static readonly StageWasCompleted Instance = new StageWasCompleted();
            private StageWasCompleted() { }
        }
    }
    
    public interface ISubscriptionWithCancelException: ISubscription
    {
        void Cancel(Exception cause);
    }    
}
