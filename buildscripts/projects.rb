#----------------------------------
# Project data for Pigeon
#----------------------------------

Projects = {
    :licenseUrl => "https://github.com/akkadotnet/akka.net/blob/master/LICENSE",
    :projectUrl => "https://github.com/akkadotnet/akka.net",
    :language => "en-US",

    :akka => {
        :id => "Akka",
        :dir => "Akka",
        :title => "Akka.NET Distributed Actor Framework",
        :description => "Akka.NET is a port of Akka actors for .NET.",
        :copyright => " (c) Roger Alsing 2013-2014",
        :authors => "Roger Alsing",
        :test_dir => "Akka.Test",
        :dependencies => {
            :newtonsoft_json => {
                :package => "Newtonsoft.Json",
                :version => "6.0.1"
           },
         },
         :nuget_tags => "akka actors actor model Akka concurrency",
       },
    :akka_fsharp => {
        :id => "Akka.FSharp",
        :dir => "Akka.FSharp",
        :title => "Akka.NET Distributed Actor Framework - F\# API",
        :description => "FSharp API support for Akka.",
        :copyright => " (c) Roger Alsing 2013-2014",
        :authors => "Roger Alsing",
        :nuget_tags => "akka actors actor model Akka concurrency",
        :framework_assemblies => {
           :fsharp_core => {
            :assemblyName => "FSharp.Core",
            :targetFramework => "net45"
           },
        }
      },
    :akka_remote => {
        :id => "Akka.Remote",
        :dir => "Akka.Remote",
        :title => "Akka.NET Distributed Actor Framework - Remote Actors",
        :description => "Remote actor support for Akka.",
        :copyright => " (c) Roger Alsing 2013-2014",
        :authors => "Roger Alsing",
        :nuget_tags => "akka Akka distributed actors remote actor model concurrency",
        :dependencies => {
            :google_protocolbuffers => {
                :package => "Google.ProtocolBuffers",
                :version => "2.4.1.521"
           },
           :helios => {
                :package => "Helios",
                :version => "1.1.5"
           }
       },
   },
   :akka_slf4net => {
        :id => "Akka.slf4net",
        :dir => "Akka.slf4net",
        :title => "slf4net logging adapter for Akka",
        :description => "slf4net logging adapter for Akka",
        :copyright => " (c) Roger Alsing 2013-2014",
        :authors => "Roger Alsing",
        :nuget_tags => "akka dlog4net slf4net akka akka",
        :dependencies => {
            :slf4net => {
                :package => "slf4net",
                :version => "0.1.32.1"
           },
       }
   },
}