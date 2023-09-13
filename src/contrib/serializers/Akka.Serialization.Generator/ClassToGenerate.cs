// -----------------------------------------------------------------------
// <copyright file="EnumToGenerate.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

namespace Akka.Serialization.Generator
{
    public sealed class ClassToGenerate
    {
        public ClassToGenerate(string fullNamespace, string @namespace, string className, string manifest)
        {
            FullNamespace = fullNamespace;
            Namespace = @namespace;
            ClassName = className;
            Manifest = manifest;
        }

        public string FullNamespace { get; }
        public string Namespace { get; }
        public string ClassName { get; }
        public string Manifest { get; }
    }
}

