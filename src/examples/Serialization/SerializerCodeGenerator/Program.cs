// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Serialization;
using Akka.Serialization.Generated;

namespace SerializerCodeGenerator;

public static class Program
{
    private const string Payload = "My Payload";
    
    public static async Task Main(string[] args)
    {
        var setup = ActorSystemSetup.Empty.AddGeneratedSerializers();
        var sys = ActorSystem.Create("mySystem", setup);

        try
        {
            var serializer = (SerializerWithStringManifest)sys.Serialization.FindSerializerForType(typeof(RemoteCommand));

            if(serializer is not SerializerCodeGeneratorSerializer)
                throw new Exception("NOT GENERATED SERIALIZER");

            var expected = new RemoteCommand { Payload = "My Payload" };
            var serialized = serializer.ToBinary(expected);
            var manifest = serializer.Manifest(expected);

            if(manifest != "A")
                throw new Exception("INVALID MANIFEST");

            var deserialized = (RemoteCommand)serializer.FromBinary(serialized, manifest);

            if (deserialized.Payload != expected.Payload)
                throw new Exception("FAILED");
        }
        finally
        {
            await sys.Terminate();
        }
    }
}