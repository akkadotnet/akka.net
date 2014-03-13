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
            :newtonsoft_json => {
                :package => "Newtonsoft.Json",
                :version => "6.0.1"
           },
         },
         :nuget_tags => "akka actors actor model pigeon concurrency",
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
        :dependencies => {
            :google_protocolbuffers => {
                :package => "Google.ProtocolBuffers",
                :version => "2.4.1.521"
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