// -----------------------------------------------------------------------
// <copyright file="SerializerInfo.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akka.Serialization.Generator;

public sealed class SerializerInfo
{
    public static readonly SerializerInfo Empty = new (null!);

    public SerializerInfo(ClassDeclarationSyntax syntax)
    {
        Syntax = syntax;
    }

    public ClassDeclarationSyntax Syntax { get; }
}    
