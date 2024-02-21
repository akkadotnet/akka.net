// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Serialization;
using Akka.Serialization.Generated;
using SerializerCodeGenerator.CommandMessages;

namespace SerializerCodeGenerator;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var setup = ActorSystemSetup.Empty.AddGeneratedSerializers();
        var sys = ActorSystem.Create("mySystem", setup);

        try
        {
            var serializer = (SerializerWithStringManifest)sys.Serialization.FindSerializerForType(typeof(CreateProduct));
            Console.WriteLine($"Serializer: {serializer.GetType()}");

            var expected = new CreateProduct
            {
                Id = 1,
                Name = "My Product",
                Amount = 1000
            };
            var serialized = serializer.ToBinary(expected);
            var manifest = serializer.Manifest(expected);

            Console.WriteLine($"Manifest for class {typeof(CreateProduct)} is {manifest}");

            var deserialized = (CreateProduct)serializer.FromBinary(serialized, manifest);

            Console.WriteLine($"Expected data: {expected}");
            Console.WriteLine($"Deserialized data: {deserialized}");
        }
        finally
        {
            await sys.Terminate();
        } 
    }
}