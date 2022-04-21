//-----------------------------------------------------------------------
// <copyright file="IMatchCompiler.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace Akka.Tools.MatchHandler
{
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    internal interface IMatchCompiler<in T>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handlers">TBD</param>
        /// <param name="capturedArguments">TBD</param>
        /// <param name="signature">TBD</param>
        /// <returns>TBD</returns>
        PartialAction<T> Compile(IReadOnlyList<TypeHandler> handlers, IReadOnlyList<Argument> capturedArguments, MatchBuilderSignature signature);
    }
}

