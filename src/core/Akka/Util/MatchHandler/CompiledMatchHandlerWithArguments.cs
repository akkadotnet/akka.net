//-----------------------------------------------------------------------
// <copyright file="CompiledMatchHandlerWithArguments.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Tools.MatchHandler
{
    public class CompiledMatchHandlerWithArguments
    {
        private readonly Delegate _compiledDelegate;
        private readonly object[] _delegateArguments;

        public CompiledMatchHandlerWithArguments(Delegate compiledDelegate, object[] delegateArguments)
        {
            _compiledDelegate = compiledDelegate;
            _delegateArguments = delegateArguments;
        }

        public Delegate CompiledDelegate { get { return _compiledDelegate; } }

        public object[] DelegateArguments { get { return _delegateArguments; } }
    }
}

