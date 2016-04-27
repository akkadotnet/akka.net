//-----------------------------------------------------------------------
// <copyright file="IMatchCompiler.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace Akka.Tools.MatchHandler
{
    public interface IMatchCompiler<in T>
    {
        PartialAction<T> Compile(IReadOnlyList<TypeHandler> handlers, IReadOnlyList<Argument> capturedArguments, MatchBuilderSignature signature);
        void CompileToMethod(IReadOnlyList<TypeHandler> handlers, IReadOnlyList<Argument> capturedArguments, MatchBuilderSignature signature, TypeBuilder typeBuilder, string methodName, MethodAttributes methodAttributes = MethodAttributes.Public | MethodAttributes.Static);
    }
}

