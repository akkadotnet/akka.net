// -----------------------------------------------------------------------
// <copyright file="EnumToGenerate.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

namespace Akka.Serialization.Generator
{
    public sealed class ClassToGenerate
    {
        public ClassToGenerate(string fullNamespace, string @namespace, string className)
        {
            FullNamespace = fullNamespace;
            Namespace = @namespace;
            ClassName = className;
        }

        public string FullNamespace { get; }
        public string Namespace { get; }
        public string ClassName { get; }
    }
}

