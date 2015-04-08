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