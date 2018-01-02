//-----------------------------------------------------------------------
// <copyright file="ArrayExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Akka.Util.Internal
{
    public static class SynchronizationContextManager
    {
        public static ContextRemover RemoveContext { get; } = new ContextRemover();
    }

    public class ContextRemover : INotifyCompletion
    {
        public bool IsCompleted => SynchronizationContext.Current == null;

        public void OnCompleted(Action continuation)
        {
            var prevContext = SynchronizationContext.Current;

            try
            {
                SynchronizationContext.SetSynchronizationContext(null);
                continuation();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(prevContext);
            }
        }

        public ContextRemover GetAwaiter()
        {
            return this;
        }

        public void GetResult()
        {

        }
    }
}
