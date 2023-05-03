//-----------------------------------------------------------------------
// <copyright file="CompiledMatchHandlerWithArguments.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Tools.MatchHandler
{
    /// <summary>
    /// TBD
    /// </summary>
    internal class CompiledMatchHandlerWithArguments
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="compiledDelegate">TBD</param>
        /// <param name="delegateArguments">TBD</param>
        public CompiledMatchHandlerWithArguments(Delegate compiledDelegate, object[] delegateArguments)
        {
            CompiledDelegate = compiledDelegate;
            DelegateArguments = delegateArguments;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Delegate CompiledDelegate { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public object[] DelegateArguments { get; }
    }
}

