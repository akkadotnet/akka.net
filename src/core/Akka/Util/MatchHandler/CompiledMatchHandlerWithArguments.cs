//-----------------------------------------------------------------------
// <copyright file="CompiledMatchHandlerWithArguments.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        private readonly Delegate _compiledDelegate;
        private readonly object[] _delegateArguments;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="compiledDelegate">TBD</param>
        /// <param name="delegateArguments">TBD</param>
        public CompiledMatchHandlerWithArguments(Delegate compiledDelegate, object[] delegateArguments)
        {
            _compiledDelegate = compiledDelegate;
            _delegateArguments = delegateArguments;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Delegate CompiledDelegate { get { return _compiledDelegate; } }

        /// <summary>
        /// TBD
        /// </summary>
        public object[] DelegateArguments { get { return _delegateArguments; } }
    }
}

