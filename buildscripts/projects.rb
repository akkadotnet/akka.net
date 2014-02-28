#----------------------------------
# Project data for Pigeon
#----------------------------------

Projects = {
    :licenseUrl => "https://github.com/rogeralsing/Pigeon/blob/master/LICENSE",
    :projectUrl => "https://github.com/rogeralsing/Pigeon",
    :language => "en-US",

    :pigeon => {
        :id => "Akka",
        :dir => "Pigeon",
        :title => "Akka.NET Distributed Actor Framework",
        :description => "Akka.NET is a port of Akka actors for .NET.",
        :copyright => " (c) Roger Alsing 2013-2014",
        :authors => "Roger Alsing",
        :test_dir => "Pigeon.Test",
        :dependencies => {
            :fast_json => {
                :package => "fastJSON",
                :version => "2.0.27.1"
           },
           :protobuf_net => {
                :package => "protobuf-net",
                :version => "2.0.0.668"
           },
         :nuget_tags => "akka actors actor model pigeon concurrency",
       },
      },
    :pigeon_fsharp => {
        :id => "Akka.FSharp",
        :dir => "Pigeon.FSharp",
        :title => "Akka.NET Distributed Actor Framework - F\# API",
        :description => "FSharp API support for Pigeon.",
        :copyright => " (c) Roger Alsing 2013-2014",
        :authors => "Roger Alsing",
        :nuget_tags => "akka actors actor model pigeon concurrency",
        :framework_assemblies => {
           :fsharp_core => {
            :assemblyName => "FSharp.Core",
            :targetFramework => "net45"
           },
        }
      },
    :pigeon_remote => {
        :id => "Akka.Remote",
        :dir => "Pigeon.Remote",
        :title => "Akka.NET Distributed Actor Framework - Remote Actors",
        :description => "Remote actor support for Pigeon.",
        :copyright => " (c) Roger Alsing 2013-2014",
        :authors => "Roger Alsing",
        :nuget_tags => "akka distributed actors remote actor model pigeon concurrency",
        :references => {
            :google_protobuf => {
                :file => "Google.ProtocolBuffers",
                :target => "lib\\net45\\"
           },
           :google_protobuf_serialization => {
                :file => "Google.ProtocolBuffers.Serialization",
                :target => "lib\\net45\\"
           },
       },
   },
   :akka_slf4net => {
        :id => "Akka.slf4net",
        :dir => "Akka.slf4net",
        :title => "slf4net logging adapter for Akka",
        :description => "slf4net logging adapter for Akka",
        :copyright => " (c) Roger Alsing 2013-2014",
        :authors => "Roger Alsing",
        :nuget_tags => "akka dlog4net slf4net akka pigeon",
        :dependencies => {
            :slf4net => {
                :package => "slf4net",
                :version => "0.1.32.1"
           },
       }
   },
}