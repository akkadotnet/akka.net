//-----------------------------------------------------------------------
// <copyright file="IMatchCompiler.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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

#if !CORECLR
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handlers">TBD</param>
        /// <param name="capturedArguments">TBD</param>
        /// <param name="signature">TBD</param>
        /// <param name="typeBuilder">TBD</param>
        /// <param name="methodName">TBD</param>
        /// <param name="methodAttributes">TBD</param>
        /// <returns>TBD</returns>
        void CompileToMethod(IReadOnlyList<TypeHandler> handlers, IReadOnlyList<Argument> capturedArguments, MatchBuilderSignature signature, TypeBuilder typeBuilder, string methodName, MethodAttributes methodAttributes = MethodAttributes.Public | MethodAttributes.Static);
#endif
    }
}

