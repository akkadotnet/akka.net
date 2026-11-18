//-----------------------------------------------------------------------
// <copyright file="AkkaSerializerGenerator.IgnoredFields.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace Akka.Serialization.V2.Generators;

public sealed partial class AkkaSerializerGenerator
{
    internal readonly record struct IgnoredFieldDeclaration(TypeKey Key, LocationBag Locations);

    private static IgnoredFieldDeclaration? ExtractIgnoredFieldDeclaration(
        GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var symbol = context.SemanticModel.GetDeclaredSymbol(context.Node, cancellationToken);
        var property = symbol as IPropertySymbol;
        if (symbol is IParameterSymbol parameter && parameter.ContainingType is { IsRecord: true } recordType)
            property = recordType.GetMembers(parameter.Name).OfType<IPropertySymbol>().FirstOrDefault();
        if (property == null)
            return null;

        var knownTypes = GetKnownTypes(context.SemanticModel.Compilation);
        if (!property.GetAttributes().Any(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, knownTypes.FieldAttribute)))
            return null;

        var type = property.ContainingType;
        foreach (var attribute in type.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, knownTypes.SerializableAttribute))
                return null;
        }

        var key = TypeKey.FromSymbol(type);
        return new IgnoredFieldDeclaration(key, BuildTypeOnlyLocationBag(type, key));
    }
}
