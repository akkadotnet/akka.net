#I @"tools/FAKE/tools"
#r "FakeLib.dll"

open System
open System.IO
open System.Text

open Fake
open Fake.DotNetCli
open Fake.DocFxHelper

// Variables
let configuration = "Release"
let solution = "./src/Akka.sln"
let versionSuffix = getBuildParamOrDefault "versionsuffix" ""

// Directories
let toolsDir = __SOURCE_DIRECTORY__ @@ "tools"
let output = __SOURCE_DIRECTORY__  @@ "bin"
let outputTests = __SOURCE_DIRECTORY__ @@ "TestResults"
let outputPerfTests = __SOURCE_DIRECTORY__ @@ "PerfResults"
let outputBinaries = output @@ "binaries"
let outputNuGet = output @@ "nuget"
let outputMultiNode = output @@ "multinode"
let outputBinariesNet45 = outputBinaries @@ "net45"
let outputBinariesNetStandard = outputBinaries @@ "netstandard1.6"

Target "Clean" (fun _ ->
    CleanDir output
    CleanDir outputTests
    CleanDir outputPerfTests
    CleanDir outputBinaries
    CleanDir outputNuGet
    CleanDir outputMultiNode
    CleanDir outputBinariesNet45
    CleanDir outputBinariesNetStandard
    CleanDir "docs/_site"

    CleanDirs !! "./**/bin"
    CleanDirs !! "./**/obj"
)

Target "RestorePackages" (fun _ ->
    let additionalArgs = if versionSuffix.Length > 0 then [sprintf "/p:VersionSuffix=%s" versionSuffix] else []  

    DotNetCli.Restore
        (fun p -> 
            { p with
                Project = solution
                NoCache = false
                AdditionalArgs = additionalArgs })
)

Target "Build" (fun _ ->
    let projects = !! "./**/core/**/*.csproj"
                   ++ "./**/contrib/**/*.csproj"
                   -- "./**/serializers/**/*Wire*.csproj"

    let runSingleProject project =
        DotNetCli.Build
            (fun p -> 
                { p with
                    Project = project
                    Configuration = configuration })

    projects |> Seq.iter (runSingleProject)
)

//--------------------------------------------------------------------------------
// Tests targets 
//--------------------------------------------------------------------------------

Target "RunTests" (fun _ ->
    let projects =
        match isWindows with
        // Windows
        | true -> !! "./**/core/**/*.Tests.csproj"
                  ++ "./**/contrib/**/*.Tests.csproj"
                  -- "./**/Akka.Streams.Tests.csproj"
                  -- "./**/Akka.Remote.TestKit.Tests.csproj"
                  -- "./**/Akka.MultiNodeTestRunner.Shared.Tests.csproj"
                  -- "./**/serializers/**/*Wire*.csproj"
                  -- "./**/Akka.Persistence.Tests.csproj"                 
        // Linux/Mono
        | _ -> !! "./**/core/**/*.Tests.csproj"
                  ++ "./**/contrib/**/*.Tests.csproj"
                  -- "./**/serializers/**/*Wire*.csproj"
                  -- "./**/Akka.Streams.Tests.csproj"
                  -- "./**/Akka.Remote.TestKit.Tests.csproj"
                  -- "./**/Akka.MultiNodeTestRunner.Shared.Tests.csproj"      
                  -- "./**/Akka.Persistence.Tests.csproj"
                  -- "./**/Akka.API.Tests.csproj"

    let runSingleProject project =
        DotNetCli.RunCommand
            (fun p -> 
                { p with 
                    WorkingDir = (Directory.GetParent project).FullName
                    TimeOut = TimeSpan.FromMinutes 30. })
                (sprintf "xunit -parallel none -teamcity -xml %s_xunit.xml" (outputTests @@ fileNameWithoutExt project)) 

    CreateDir outputTests
    projects |> Seq.iter (runSingleProject)
)

Target "MultiNodeTests" (fun _ ->
    let multiNodeTestPath = findToolInSubPath "Akka.MultiNodeTestRunner.exe" (currentDirectory @@ "src" @@ "core" @@ "Akka.MultiNodeTestRunner" @@ "bin" @@ "Release" @@ "net452")
    let multiNodeTestAssemblies = !! "src/**/bin/Release/**/Akka.Remote.Tests.MultiNode.dll" ++
                                     "src/**/bin/Release/**/Akka.Cluster.Tests.MultiNode.dll" ++
                                     "src/**/bin/Release/**/Akka.Cluster.Tools.Tests.MultiNode.dll" ++
                                     "src/**/bin/Release/**/Akka.Cluster.Sharding.Tests.MultiNode.dll" ++
                                     "src/**/bin/Release/**/Akka.DistributedData.Tests.MultiNode.dll"

    printfn "Using MultiNodeTestRunner: %s" multiNodeTestPath

    let runMultiNodeSpec assembly =
        let spec = getBuildParam "spec"

        let args = StringBuilder()
                |> append assembly
                |> append "-Dmultinode.teamcity=true"
                |> append "-Dmultinode.enable-filesink=on"
                |> append (sprintf "-Dmultinode.output-directory=\"%s\"" outputMultiNode)
                |> appendIfNotNullOrEmpty spec "-Dmultinode.spec="
                |> toText

        let result = ExecProcess(fun info -> 
            info.FileName <- multiNodeTestPath
            info.WorkingDirectory <- (Path.GetDirectoryName (FullName multiNodeTestPath))
            info.Arguments <- args) (System.TimeSpan.FromMinutes 60.0) (* This is a VERY long running task. *)
        if result <> 0 then failwithf "MultiNodeTestRunner failed. %s %s" multiNodeTestPath args
    
    multiNodeTestAssemblies |> Seq.iter (runMultiNodeSpec)
)

Target "NBench" <| fun _ ->
    CleanDir outputPerfTests
    // .NET Framework
    let testSearchPath =
        let assemblyFilter = getBuildParamOrDefault "spec-assembly" String.Empty
        sprintf "src/**/bin/Release/**/*%s*.Tests.Performance.dll" assemblyFilter

    let nbenchTestPath = findToolInSubPath "NBench.Runner.exe" (toolsDir @@ "NBench.Runner*")
    let nbenchTestAssemblies = !! testSearchPath
    printfn "Using NBench.Runner: %s" nbenchTestPath

    let runNBench assembly =
        let spec = getBuildParam "spec"
        let teamcityStr = (getBuildParam "teamcity")
        let enableTeamCity = 
            match teamcityStr with
            | null -> false
            | "" -> false
            | _ -> bool.Parse teamcityStr

        let args = StringBuilder()
                |> append assembly
                |> append (sprintf "output-directory=\"%s\"" outputPerfTests)
                |> append (sprintf "concurrent=\"%b\"" true)
                |> append (sprintf "trace=\"%b\"" true)
                |> append (sprintf "teamcity=\"%b\"" enableTeamCity)
                |> toText

        let result = ExecProcess(fun info -> 
            info.FileName <- nbenchTestPath
            info.WorkingDirectory <- (Path.GetDirectoryName (FullName nbenchTestPath))
            info.Arguments <- args) (System.TimeSpan.FromMinutes 45.0) (* Reasonably long-running task. *)
        if result <> 0 then failwithf "NBench.Runner failed. %s %s" nbenchTestPath args
    
    nbenchTestAssemblies |> Seq.iter (runNBench)

//--------------------------------------------------------------------------------
// Nuget targets 
//--------------------------------------------------------------------------------

Target "CreateNuget" (fun _ ->
    let projects = !! "src/**/Akka.csproj"
                   ++ "src/**/Akka.Cluster.csproj"
                   ++ "src/**/Akka.Cluster.TestKit.csproj"
                   ++ "src/**/Akka.Cluster.Tools.csproj"
                   ++ "src/**/Akka.Cluster.Sharding.csproj"
                   ++ "src/**/Akka.DistributedData.csproj"
                   ++ "src/**/Akka.DistributedData.LightningDB.csproj"
                   ++ "src/**/Akka.Persistence.csproj"
                   ++ "src/**/Akka.Persistence.Query.csproj"
                   ++ "src/**/Akka.Persistence.TestKit.csproj"
                   ++ "src/**/Akka.Persistence.Query.Sql.csproj"
                   ++ "src/**/Akka.Persistence.Sql.Common.csproj"
                   ++ "src/**/Akka.Persistence.Sql.TestKit.csproj"
                   ++ "src/**/Akka.Persistence.Sqlite.csproj"
                   ++ "src/**/Akka.Remote.csproj"
                   ++ "src/**/Akka.Remote.TestKit.csproj"
                   ++ "src/**/Akka.Streams.csproj"
                   ++ "src/**/Akka.Streams.TestKit.csproj"
                   ++ "src/**/Akka.TestKit.csproj"
                   ++ "src/**/Akka.TestKit.Xunit2.csproj"
                   ++ "src/**/Akka.DI.Core.csproj"
                   ++ "src/**/Akka.DI.TestKit.csproj"
                   ++ "src/**/Akka.Serialization.Hyperion.csproj"
                   ++ "src/**/Akka.Serialization.TestKit.csproj"
                   ++ "src/**/Akka.Remote.Transport.Helios.csproj"

    let runSingleProject project =
        DotNetCli.Pack
            (fun p -> 
                { p with
                    Project = project
                    Configuration = configuration
                    AdditionalArgs = ["--include-symbols"]
                    VersionSuffix = versionSuffix
                    OutputPath = outputNuGet })

    projects |> Seq.iter (runSingleProject)
)

Target "PublishNuget" (fun _ ->
    let projects = !! "./build/nuget/*.nupkg" -- "./build/nuget/*.symbols.nupkg"
    let apiKey = getBuildParamOrDefault "nugetkey" ""
    let source = getBuildParamOrDefault "nugetpublishurl" ""
    let symbolSource = getBuildParamOrDefault "symbolspublishurl" ""

    let runSingleProject project =
        DotNetCli.RunCommand
            (fun p -> 
                { p with 
                    TimeOut = TimeSpan.FromMinutes 10. })
            (sprintf "nuget push %s --api-key %s --source %s --symbol-source %s" project apiKey source symbolSource)

    projects |> Seq.iter (runSingleProject)
)

//--------------------------------------------------------------------------------
// Serialization
//--------------------------------------------------------------------------------
Target "Protobuf" <| fun _ ->
    let protocPath = findToolInSubPath "protoc.exe" "src/packages/Google.Protobuf.Tools/tools/windows_x64"

    let protoFiles = [
        ("WireFormats.proto", "/src/core/Akka.Remote/Serialization/Proto/");
        ("ContainerFormats.proto", "/src/core/Akka.Remote/Serialization/Proto/");
        ("ContainerFormats.proto", "/src/core/Akka.Remote/Serialization/Proto/");
        ("SystemMessageFormats.proto", "/src/core/Akka.Remote/Serialization/Proto/");
        ("ClusterMessages.proto", "/src/core/Akka.Cluster/Serialization/Proto/");
        ("ClusterClientMessages.proto", "/src/contrib/cluster/Akka.Cluster.Tools/Client/Serialization/Proto/");
        ("DistributedPubSubMessages.proto", "/src/contrib/cluster/Akka.Cluster.Tools/PublishSubscribe/Serialization/Proto/");
        ("ClusterShardingMessages.proto", "/src/contrib/cluster/Akka.Cluster.Sharding/Serialization/Proto/");
        ("TestConductorProtocol.proto", "/src/core/Akka.Remote.TestKit/Proto/") ]

    printfn "Using proto.exe: %s" protocPath

    let runProtobuf assembly =
        let protoName, destinationPath = assembly
        let args = StringBuilder()
                |> append (sprintf "-I=%s;%s" (__SOURCE_DIRECTORY__ @@ "/src/protobuf/") (__SOURCE_DIRECTORY__ @@ "/src/protobuf/common") )
                |> append (sprintf "--csharp_out=%s" (__SOURCE_DIRECTORY__ @@ destinationPath))
                |> append "--csharp_opt=file_extension=.g.cs"
                |> append (__SOURCE_DIRECTORY__ @@ "/src/protobuf" @@ protoName)
                |> toText

        let result = ExecProcess(fun info -> 
            info.FileName <- protocPath
            info.WorkingDirectory <- (Path.GetDirectoryName (FullName protocPath))
            info.Arguments <- args) (System.TimeSpan.FromMinutes 45.0) (* Reasonably long-running task. *)
        if result <> 0 then failwithf "protoc failed. %s %s" protocPath args
    
    protoFiles |> Seq.iter (runProtobuf)

//--------------------------------------------------------------------------------
// Documentation 
//--------------------------------------------------------------------------------  
Target "DocFx" <| fun _ ->
    let docFxToolPath = findToolInSubPath "docfx.exe" "./tools/docfx.console/tools" 

    let docsPath = "./docs"

    DocFx (fun p -> 
                { p with 
                    Timeout = TimeSpan.FromMinutes 5.0; 
                    WorkingDirectory  = docsPath; 
                    DocFxJson = docsPath @@ "docfx.json" })

//--------------------------------------------------------------------------------
// Help 
//--------------------------------------------------------------------------------

Target "Help" <| fun _ ->
    List.iter printfn [
      "usage:"
      "/build [target]"
      ""
      " Targets for building:"
      " * Build      Builds"
      " * Nuget      Create and optionally publish nugets packages"
      " * RunTests   Runs tests"
      " * All        Builds, run tests, creates and optionally publish nuget packages"
      ""
      " Other Targets"
      " * Help       Display this help" 
      ""]

Target "HelpNuget" <| fun _ ->
    List.iter printfn [
      "usage: "
      "build Nuget [nugetkey=<key> [nugetpublishurl=<url>]] "
      "            [symbolspublishurl=<url>] "
      ""
      "In order to publish a nuget package, keys must be specified."
      "If a key is not specified the nuget packages will only be created on disk"
      "After a build you can find them in build/nuget"
      ""
      "For pushing nuget packages to nuget.org and symbols to symbolsource.org"
      "you need to specify nugetkey=<key>"
      "   build Nuget nugetKey=<key for nuget.org>"
      ""
      "For pushing the ordinary nuget packages to another place than nuget.org specify the url"
      "  nugetkey=<key>  nugetpublishurl=<url>  "
      ""
      "For pushing symbols packages specify:"
      "  symbolskey=<key>  symbolspublishurl=<url> "
      ""
      "Examples:"
      "  build Nuget                      Build nuget packages to the build/nuget folder"
      ""
      "  build Nuget versionsuffix=beta1  Build nuget packages with the custom version suffix"
      ""
      "  build Nuget nugetkey=123         Build and publish to nuget.org and symbolsource.org"
      ""
      "  build Nuget nugetprerelease=dev nugetkey=123 nugetpublishurl=http://abcsymbolspublishurl=http://xyz"
      ""]

//--------------------------------------------------------------------------------
//  Target dependencies
//--------------------------------------------------------------------------------

Target "BuildRelease" DoNothing
Target "All" DoNothing
Target "Nuget" DoNothing

// build dependencies
"Clean" ==> "RestorePackages" ==> "Build" ==> "BuildRelease"

// tests dependencies
"Clean" ==> "RestorePackages" ==> "RunTests"

// nuget dependencies
"Clean" ==> "RestorePackages" ==> "Build" ==> "CreateNuget"
"CreateNuget" ==> "PublishNuget"
"PublishNuget" ==> "Nuget"

// docs
"Clean" ==> "Docfx"

// all
"BuildRelease" ==> "All"
"RunTests" ==> "All"
"MultiNodeTests" ==> "All"
"NBench" ==> "All"

RunTargetOrDefault "Help"
