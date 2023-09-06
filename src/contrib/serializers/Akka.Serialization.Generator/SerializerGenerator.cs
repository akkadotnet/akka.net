//-----------------------------------------------------------------------
// <copyright file="SerializerGenerator.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Akka.Serialization.Generator;

[Generator(LanguageNames.CSharp)]
public class SerializerGenerator: IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(ctx =>
            {
                ctx.AddSource("GeneratedSerializerExtensions_Base.g.cs",
                    SourceText.From(StaticCodes.ExtensionsClass, Encoding.UTF8));
            });
        
        var classDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsSyntaxTargetForGeneration(s), // select classes with attributes
                transform: static (ctx, _) => GetSemanticTargetForGeneration(ctx)) // get classes with the [AkkaSerialize] attribute
            .Where(m => !ReferenceEquals(m, SerializerInfo.Empty)); // filter out attributed classes that we don't care about
            
        // Combine the selected classes with the `Compilation`
        var compilationAndClasses = context.CompilationProvider.Combine(classDeclarations.Collect());
            
        // Generate the source using the compilation and classes
        context.RegisterSourceOutput(compilationAndClasses,
            static (spc, source) => Execute(source.Left, source.Right, spc));
    }

    private static bool IsSyntaxTargetForGeneration(SyntaxNode node)
        => node is ClassDeclarationSyntax { AttributeLists.Count: > 0 };

    private static SerializerInfo GetSemanticTargetForGeneration(GeneratorSyntaxContext context)
    {
        // we know the node is a ClassDeclarationSyntax thanks to IsSyntaxTargetForGeneration
        var classDeclarationSyntax = (ClassDeclarationSyntax)context.Node;

        if (context.SemanticModel.GetDeclaredSymbol(context.Node) is not ITypeSymbol classModel)
        {
            // weird, we couldn't get the type symbol, ignore it
            return SerializerInfo.Empty;
        }

        foreach (var attribute in classModel.GetAttributes())
        {
            if(attribute.AttributeClass is { Name: "AkkaSerializeAttribute" } && 
               attribute.AttributeClass.ContainingNamespace.ToString() is "Akka.Serialization")
            {
                return new SerializerInfo(classDeclarationSyntax);
            }
        }

        // we didn't find the attribute we were looking for
        return SerializerInfo.Empty;
    }
        
    private static void Execute(Compilation compilation, ImmutableArray<SerializerInfo> classes, SourceProductionContext context)
    {
        if (classes.IsDefaultOrEmpty)
            return; // nothing to do yet

        // Convert each ClassDeclarationSyntax to an ClassToGenerate
        var classesToGenerate = GetTypesToGenerate(compilation, classes, context.CancellationToken);

        // If there were errors in the EnumDeclarationSyntax, we won't create an
        // EnumToGenerate for it, so make sure we have something to generate
        if (classesToGenerate.Count == 0)
            return;

        var generated = new List<(string, string, List<ClassToGenerate>)>();
        var id = 1001;
        foreach (var namespaceGroup in classesToGenerate.GroupBy(c => c.FullNamespace))
        {
            var boundClasses = namespaceGroup.ToList();
            if(boundClasses.Count == 0)
                continue;
            
            var namespaceName = namespaceGroup.Key;
            var serializerName = $"{boundClasses[0].Namespace}Serializer";
            
            // generate the source code and add it to the output
            var result = GenerateSerializerClass(namespaceName, serializerName, id, boundClasses);
            context.AddSource($"{namespaceName}.{serializerName}.g.cs", SourceText.From(result, Encoding.UTF8));
            id++;
            generated.Add((serializerName, $"{namespaceName}.{serializerName}", boundClasses));
        }

        context.AddSource(
            "GeneratedSerializerExtensions.g.cs", SourceText.From(GenerateExtensionClass(generated), Encoding.UTF8));
    }
        
    private static List<ClassToGenerate> GetTypesToGenerate(
        Compilation compilation,
        IEnumerable<SerializerInfo> classes,
        CancellationToken ct)
    {
        // Create a list to hold our output
        var classesToGenerate = new List<ClassToGenerate>();
        // Get the semantic representation of our marker attribute 
        var classAttribute = compilation.GetTypeByMetadataName("Akka.Serialization.AkkaSerializeAttribute");

        if (classAttribute == null)
        {
            // If this is null, the compilation couldn't find the marker attribute type
            // which suggests there's something very wrong! Bail out..
            return classesToGenerate;
        }

        foreach (var classInfo in classes)
        {
            // stop if we're asked to
            ct.ThrowIfCancellationRequested();

            // Get the semantic representation of the enum syntax
            var semanticModel = compilation.GetSemanticModel(classInfo.Syntax.SyntaxTree);
            if (semanticModel.GetDeclaredSymbol(classInfo.Syntax, cancellationToken: ct) is not INamedTypeSymbol classSymbol)
            {
                // something went wrong, bail out
                continue;
            }

            //var className = classSymbol.Name;
            var @namespace = classSymbol.ContainingNamespace;
            
            // Create a ClassToGenerate for use in the generation phase
            classesToGenerate.Add(new ClassToGenerate(@namespace.ToString(), @namespace.Name, classSymbol.Name));
        }

        return classesToGenerate;
    }

    private static string GenerateExtensionClass(List<(string, string, List<ClassToGenerate>)> generated)
    {
        var details = new StringBuilder();
        var aliasMap = new StringBuilder();
        var bindings = new StringBuilder();
        
        foreach (var (alias, @class, classes) in generated)
        {
            details.AppendLine($"            SerializerDetails.Create(\"{alias}\", new {@class}(sys), new[]{{ {string.Join(", ", classes.Select(s => $"typeof({s.FullNamespace}.{s.ClassName})"))} }}.ToImmutableHashSet()),");
            aliasMap.AppendLine($"            [\"{alias}\"] = typeof({@class}),");
            foreach (var c in classes)
            {
                bindings.AppendLine($"            [typeof({c.FullNamespace}.{c.ClassName})] = \"{alias}\",");
            }
        }

        return $$"""
                 {{StaticCodes.Header}}
                 
                 using System;
                 using System.Collections.Generic;
                 using System.Collections.Immutable;
                 using Akka.Actor;
                 using Akka.Serialization;
                 
                 namespace Akka.Serialization.Generated;

                 public static partial class GeneratedSerializerExtensions
                 {
                     static partial void GenerateDetails(ExtendedActorSystem sys)
                     {
                         _serializerDetails = new SerializerDetails[]
                         {
                 {{details}}
                         };
                     }
                 
                     static partial void GenerateHocon()
                     {
                         _aliasMap = new Dictionary<string, Type> {
                 {{aliasMap}}
                         };
                         _bindMap = new Dictionary<Type, string> {
                 {{bindings}}
                         };
                     }
                 }
                 """;
    }
        
    private static string GenerateSerializerClass(
        string namespaceName,
        string serializerName,
        int id,
        List<ClassToGenerate> classesToGenerate)
    {
        var classCount = 1L;
        var sb = new StringBuilder();
        sb.Append(
            $$"""
              {{StaticCodes.Header}}
              
              using System;
              using System.IO;
              using Akka.Actor;
              using Akka.Serialization;
              using Newtonsoft.Json;
              using Newtonsoft.Json.Bson;

              namespace {{namespaceName}};

              public class {{serializerName}} : SerializerWithStringManifest
              {
                  private readonly JsonSerializerSettings _settings;
                  private readonly JsonSerializer _serializer;

              """);

        foreach (var classToGenerate in classesToGenerate)
        {
            sb.AppendLine(
                $"""
                     public const string {classToGenerate.ClassName}Manifest = "{classCount.ToBase26()}";
                 """);
            classCount++;
        }

        sb.AppendLine(
            $$"""
              
                  public {{serializerName}}(ExtendedActorSystem system) : base(system)
                  {
                      var jsonSerializer = new NewtonSoftJsonSerializer(system);
                      _settings = jsonSerializer.Settings;
                      _serializer = JsonSerializer.Create(_settings);
                  }
                  
                  public override int Identifier => {{id}};
              
                  public override byte[] ToBinary(object obj)
                  {
                      switch(obj)
                      {
              """
        );

        foreach (var classToGenerate in classesToGenerate)
        {
            sb.AppendLine(
                $"            case {classToGenerate.FullNamespace}.{classToGenerate.ClassName}:"
            );
        }
            
        sb.AppendLine(
            $$"""
                              return ToBson(obj);
                          default:
                              throw new ArgumentException($"Can't serialize object of type [{obj.GetType()}] in [{nameof({{serializerName}})}]");
                      }
                  }
              
                  private byte[] ToBson(object obj)
                  {
                      using var stream = new MemoryStream();
                      using var writer = new BsonDataWriter(stream);
                      _serializer.Serialize(writer, obj);
                      return stream.ToArray();
                  }
              
                  private object FromBson<T>(byte[] bytes)
                  {
                      using var stream = new MemoryStream(bytes);
                      using var reader = new BsonDataReader(stream);
                      return _serializer.Deserialize<T>(reader);
                  }
              
                  public override object FromBinary(byte[] bytes, string manifest)
                  {
                      return manifest switch
                      {
              """);

        foreach (var classToGenerate in classesToGenerate)
        {
            sb.AppendLine(
                $"            {classToGenerate.ClassName}Manifest => FromBson<{classToGenerate.ClassName}>(bytes),");
        }

        sb.AppendLine(
            $$"""
                          _ => throw new ArgumentException($"Unknown manifest [{manifest}] in [{nameof({{serializerName}})}]")
                      };
                  }
              
                  public override string Manifest(object o)
                  {
                      return o switch
                      {
              """);

        foreach (var classToGenerate in classesToGenerate)
        {
            sb.AppendLine(
                $"            {classToGenerate.ClassName} => {classToGenerate.ClassName}Manifest,");
        }
            
        sb.AppendLine(
            $$"""
                          _ => throw new ArgumentException($"Can't serialize object of type [{o.GetType()}] in [{nameof({{serializerName}})}]")
                      };
                  }
              }
              """);
            
        return sb.ToString();
    }
}