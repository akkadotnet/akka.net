//-----------------------------------------------------------------------
// <copyright file="SynchronizationContextManager.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Annotations;
using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Akka.Util.Internal
{
    /// <summary>
    /// SynchronizationContextManager controls SynchronizationContext of the async pipeline.
    /// <para>Does the same thing as .ConfigureAwait(false) but better - it should be written once only,
    /// unlike .ConfigureAwait(false).</para>
    /// <para> await SynchronizationContextManager.RemoveContext; 
    /// Should be used as a very first line inside async public API of the library code</para>
    /// </summary>
    /// <example> 
    /// This sample shows how to use <see cref="SynchronizationContextManager"/> .
    /// <code>
    /// class CoolLib 
    /// {
    ///     public async Task DoSomething() 
    ///     {
    ///         await SynchronizationContextManager.RemoveContext;
    ///         
    ///         await DoSomethingElse();
    ///     }
    /// }
    /// </code>
    /// </example>
    [InternalApi]
    internal static class SynchronizationContextManager
    {
        public static ContextRemover RemoveContext { get; } = new ContextRemover();
    }

    [InternalApi]
    internal class ContextRemover : INotifyCompletion
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
            // empty on purpose
        }
    }
}
