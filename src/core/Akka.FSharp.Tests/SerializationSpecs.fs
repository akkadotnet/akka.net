module Akka.FSharp.Tests.SerializationSpecs

open Akka.Configuration
open Akka.Serialization
open Akka.TestKit
open Newtonsoft.Json
open Newtonsoft.Json
open Newtonsoft.Json.Converters
open Xunit
open Xunit.Abstractions

     type TestUnion = 
        | A of string
        | B of int * string

    type TestUnion2 = 
        | C of string * TestUnion
        | D of int
        
    type Record1 = {Name:string;Age:int;}

    type Msg =
        | R of Record1

/// Validate serialization test cases without Akka.Remote
type SerializationSpecs(output:ITestOutputHelper) as this =
    inherit AkkaSpec(Config.Empty, output) 
    static member FSharpTypes
        with get() = 
            let objects : obj list = [
                                        A "foo"
                                        C("a-11", B(11, "a-12")) // DU with tuple type
                                        D 13 // simple DU case
                                        {Name = "aaron"; Age = 30 } // record
                                        R { Name ="Ardbeg"; Age=5 } // single case DU
                                    ]
            objects |> List.map (fun case -> [| case |])

    
    /// Verifies serialization similar to how we do it for C# specs
    member this.VerifySerialization msg =
        let serializer = this.Sys.Serialization.FindSerializerFor msg
        let t = msg.GetType()
        let bytes = this.Sys.Serialization.Serialize msg
        let deserialized =
            match serializer :> obj with
            | :? SerializerWithStringManifest as str -> this.Sys.Serialization.Deserialize(bytes, serializer.Identifier, str.Manifest msg) 
            | _ -> this.Sys.Serialization.Deserialize(bytes, serializer.Identifier, t)

        Assert.Equal(msg, deserialized)
        
    [<Theory>]
    [<MemberData(nameof(SerializationSpecs.FSharpTypes))>]
    member _.``Must verify serialization of F#-specific types`` (t:obj) =
        this.VerifySerialization t
        // uncomment the section below when experimenting with IncludeManifest = true on the Newtonsoft.Json serializer
//        let s = this.Sys.Serialization.FindSerializerFor t
//        let manifest = Akka.Serialization.Serialization.ManifestFor(s, t)
//        Assert.True(manifest.Length > 0)
        
        
    [<Fact(Skip="JSON.NET really does not support even basic DU serialization")>]
    member _.``JSON.NET must serialize DUs`` () =
        let du = C("a-11", B(11, "a-12"))
        let settings = new JsonSerializerSettings()
        settings.Converters.Add(new DiscriminatedUnionConverter())
        
        let serialized = JsonConvert.SerializeObject(du, settings)
        let deserialized = JsonConvert.DeserializeObject(serialized, settings)
        
        Assert.Equal(du :> obj, deserialized)
        
    