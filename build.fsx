#I @"tools/FAKE/tools"
#r "FakeLib.dll"
#load "./buildIncremental.fsx"

open System
open System.IO
open System.Text
open System.Diagnostics

open Fake
open Fake.DotNetCli
open Fake.DocFxHelper
open Fake.Git
open Fake.NuGet.Install

// Variables
let configuration = "Release"
let solution = "./src/Akka.sln"

// Directories
let toolsDir = __SOURCE_DIRECTORY__ @@ "tools"
let output = __SOURCE_DIRECTORY__  @@ "bin"
let outputTests = __SOURCE_DIRECTORY__ @@ "TestResults"
let outputPerfTests = __SOURCE_DIRECTORY__ @@ "PerfResults"
let outputBinaries = output @@ "binaries"
let outputNuGet = output @@ "nuget"
let outputMultiNode = outputTests @@ "multinode"
let outputBinariesNet45 = outputBinaries @@ "net45"
let outputBinariesNetStandard = outputBinaries @@ "netstandard1.6"

let buildNumber = environVarOrDefault "BUILD_NUMBER" "0"
let preReleaseVersionSuffix = "beta" + (if (not (buildNumber = "0")) then (buildNumber) else "")
let versionSuffix = 
    match (getBuildParam "nugetprerelease") with
    | "dev" -> preReleaseVersionSuffix
    | _ -> ""

let releaseNotes =
    File.ReadLines "./RELEASE_NOTES.md"
    |> ReleaseNotesHelper.parseReleaseNotes

Target "Clean" (fun _ ->
    ActivateFinalTarget "KillCreatedProcesses"

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

Target "AssemblyInfo" (fun _ ->
    XmlPokeInnerText "./src/common.props" "//Project/PropertyGroup/VersionPrefix" releaseNotes.AssemblyVersion    
    XmlPokeInnerText "./src/common.props" "//Project/PropertyGroup/PackageReleaseNotes" (releaseNotes.Notes |> String.concat "\n")
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
    let additionalArgs = if versionSuffix.Length > 0 then [sprintf "/p:VersionSuffix=%s" versionSuffix] else []  

    DotNetCli.Build
        (fun p -> 
            { p with
                Project = solution
                Configuration = configuration
                AdditionalArgs = additionalArgs })
)

//--------------------------------------------------------------------------------
// Tests targets 
//--------------------------------------------------------------------------------

module internal ResultHandling =
    let (|OK|Failure|) = function
        | 0 -> OK
        | x -> Failure x

    let buildErrorMessage = function
        | OK -> None
        | Failure errorCode ->
            Some (sprintf "xUnit2 reported an error (Error Code %d)" errorCode)

    let failBuildWithMessage = function
        | DontFailBuild -> traceError
        | _ -> (fun m -> raise(FailedTestsException m))

    let failBuildIfXUnitReportedError errorLevel =
        buildErrorMessage
        >> Option.iter (failBuildWithMessage errorLevel)

open BuildIncremental.IncrementalTests

Target "RunTests" (fun _ ->    
    ActivateFinalTarget "KillCreatedProcesses"
    let projects =
        match getBuildParamOrDefault "incremental" "" with
        | "true" -> log "The following test projects would be run under Incremental Test config..."
                    getIncrementalUnitTests Net |> Seq.map (fun x -> printfn "\t%s" x; x)
        | "experimental" -> log "The following test projects would be run under Incremental Test config..."
                            (getIncrementalUnitTests Net) |> Seq.iter log
                            getUnitTestProjects Net
        | _ -> log "All test projects will be run..."
               getUnitTestProjects Net
    
    let runSingleProject project =
        let result = ExecProcess(fun info ->
            info.FileName <- "dotnet"
            info.WorkingDirectory <- (Directory.GetParent project).FullName
            info.Arguments <- (sprintf "xunit -f net452 -c Release -nobuild -parallel none -teamcity -xml %s_xunit.xml" (outputTests @@ fileNameWithoutExt project))) (TimeSpan.FromMinutes 30.)
        
        ResultHandling.failBuildIfXUnitReportedError TestRunnerErrorLevel.DontFailBuild result

        // dotnet process will be killed by ExecProcess (or throw if can't) '
        // but per https://github.com/xunit/xunit/issues/1338 xunit.console may not
        killProcess "xunit.console"
        killProcess "dotnet"

    CreateDir outputTests
    projects |> Seq.iter (runSingleProject)
)

Target "RunTestsNetCore" (fun _ ->
    ActivateFinalTarget "KillCreatedProcesses"
    let projects =
        match getBuildParamOrDefault "incremental" "" with
        | "true" -> log "The following test projects would be run under Incremental Test config..."
                    getIncrementalUnitTests NetCore |> Seq.map (fun x -> printfn "\t%s" x; x)
        | "experimental" -> log "The following test projects would be run under Incremental Test config..."
                            getIncrementalUnitTests NetCore |> Seq.iter log
                            getUnitTestProjects NetCore
        | _ -> log "All test projects will be run..."
               getUnitTestProjects NetCore
     
    let runSingleProject project =
        let result = ExecProcess(fun info ->
            info.FileName <- "dotnet"
            info.WorkingDirectory <- (Directory.GetParent project).FullName
            info.Arguments <- (sprintf "xunit -f netcoreapp1.1 -c Release -parallel none -teamcity -xml %s_xunit_netcore.xml" (outputTests @@ fileNameWithoutExt project))) (TimeSpan.FromMinutes 30.)
        
        ResultHandling.failBuildIfXUnitReportedError TestRunnerErrorLevel.DontFailBuild result

        // dotnet process will be killed by FAKE.ExecProcess (or throw if can't)
        // but per https://github.com/xunit/xunit/issues/1338 xunit.console may not be killed
        killProcess "xunit.console"
        killProcess "dotnet"

    CreateDir outputTests
    projects |> Seq.iter (runSingleProject)
)

Target "MultiNodeTests" (fun _ ->
    ActivateFinalTarget "KillCreatedProcesses"
    let multiNodeTestPath = findToolInSubPath "Akka.MultiNodeTestRunner.exe" (currentDirectory @@ "src" @@ "core" @@ "Akka.MultiNodeTestRunner" @@ "bin" @@ "Release" @@ "net452")

    let multiNodeTestAssemblies = 
        match getBuildParamOrDefault "incremental" "" with
        | "true" -> log "The following test projects would be run under Incremental Test config..."
                    getIncrementalMNTRTests() |> Seq.map (fun x -> printfn "\t%s" x; x)
        | "experimental" -> log "The following MNTR specs would be run under Incremental Test config..."
                            getIncrementalMNTRTests() |> Seq.iter log
                            getAllMntrTestAssemblies()
        | _ -> log "All test projects will be run"
               getAllMntrTestAssemblies()

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

Target "MultiNodeTestsNetCore" (fun _ ->
    ActivateFinalTarget "KillCreatedProcesses"
    let multiNodeTestPath = findToolInSubPath "Akka.MultiNodeTestRunner.dll" (currentDirectory @@ "src" @@ "core" @@ "Akka.MultiNodeTestRunner" @@ "bin" @@ "Release" @@ "netcoreapp1.1" @@ "win7-x64" @@ "publish")

    let multiNodeTestAssemblies = 
        match getBuildParamOrDefault "incremental" "" with
        | "true" -> log "The following test projects would be run under Incremental Test config..."
                    getIncrementalNetCoreMNTRTests() |> Seq.map (fun x -> printfn "\t%s" x; x)
        | "experimental" -> log "The following MNTR specs would be run under Incremental Test config..."
                            getIncrementalNetCoreMNTRTests() |> Seq.iter log
                            getAllMntrTestNetCoreAssemblies()
        | _ -> log "All test projects will be run"
               getAllMntrTestNetCoreAssemblies()

    printfn "Using MultiNodeTestRunner: %s" multiNodeTestPath

    let runMultiNodeSpec assembly =
        match assembly with
        | null -> ()
        | _ ->
            let spec = getBuildParam "spec"

            let args = StringBuilder()
                    |> append multiNodeTestPath
                    |> append assembly
                    |> append "-Dmultinode.teamcity=true"
                    |> append "-Dmultinode.enable-filesink=on"
                    |> append (sprintf "-Dmultinode.output-directory=\"%s\"" outputMultiNode)
                    |> append "-Dmultinode.platform=netcore"
                    |> appendIfNotNullOrEmpty spec "-Dmultinode.spec="
                    |> toText

            let result = ExecProcess(fun info -> 
                info.FileName <- "dotnet"
                info.WorkingDirectory <- (Path.GetDirectoryName (FullName multiNodeTestPath))
                info.Arguments <- args) (System.TimeSpan.FromMinutes 60.0) (* This is a VERY long running task. *)
            if result <> 0 then failwithf "MultiNodeTestRunner failed. %s %s" multiNodeTestPath args
    
    multiNodeTestAssemblies |> Seq.iter (runMultiNodeSpec)
)

Target "NBench" <| fun _ ->
    ActivateFinalTarget "KillCreatedProcesses"   
    CleanDir outputPerfTests

    let nbenchTestPath = findToolInSubPath "NBench.Runner.exe" (toolsDir @@ "NBench.Runner*")
    printfn "Using NBench.Runner: %s" nbenchTestPath

    let nbenchTestAssemblies = 
        match getBuildParamOrDefault "incremental" "" with
        | "true" -> log "The following test projects would be run under Incremental Test config..."
                    getIncrementalPerfTests() |> Seq.map (fun x -> printfn "\t%s" x; x)
        | "experimental" -> log "The following test projects would be run under Incremental Test config..."
                            getIncrementalPerfTests() |> Seq.iter log
                            getAllPerfTestAssemblies()
        | _ -> getAllPerfTestAssemblies()

    let runNBench assembly =
        let includes = getBuildParam "include"
        let excludes = getBuildParam "exclude"
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
                |> appendIfNotNullOrEmpty includes "include="
                |> appendIfNotNullOrEmpty excludes "include="
                |> toText

        let result = ExecProcess(fun info -> 
            info.FileName <- nbenchTestPath
            info.WorkingDirectory <- (Path.GetDirectoryName (FullName nbenchTestPath))
            info.Arguments <- args) (System.TimeSpan.FromMinutes 45.0) (* Reasonably long-running task. *)
        if result <> 0 then failwithf "NBench.Runner failed. %s %s" nbenchTestPath args
    
    nbenchTestAssemblies |> Seq.iter runNBench

//--------------------------------------------------------------------------------
// Nuget targets 
//--------------------------------------------------------------------------------

let overrideVersionSuffix (project:string) =
    match project with
    | p when p.Contains("Akka.Serialization.Wire") -> preReleaseVersionSuffix
    | p when p.Contains("Akka.Serialization.Hyperion") -> preReleaseVersionSuffix
    | p when p.Contains("Akka.Cluster.Sharding") -> preReleaseVersionSuffix
    | p when p.Contains("Akka.DistributedData") -> preReleaseVersionSuffix
    | p when p.Contains("Akka.DistributedData.LightningDB") -> preReleaseVersionSuffix
    | _ -> versionSuffix

Target "CreateNuget" (fun _ ->    
    let projects = !! "src/**/*.csproj"
                   -- "src/**/*.Tests*.csproj"
                   -- "src/benchmark/**/*.csproj"
                   -- "src/examples/**/*.csproj"
                   -- "src/**/*.MultiNodeTestRunner.csproj"
                   -- "src/**/*.MultiNodeTestRunner.Shared.csproj"
                   -- "src/**/*.NodeTestRunner.csproj"

    let runSingleProject project =
        DotNetCli.Pack
            (fun p -> 
                { p with
                    Project = project
                    Configuration = configuration
                    AdditionalArgs = ["--include-symbols"]
                    VersionSuffix = overrideVersionSuffix project
                    OutputPath = outputNuGet })

    projects |> Seq.iter (runSingleProject)
)
open Fake.TemplateHelper
Target "PublishMntr" (fun _ ->
    let executableProjects = !! "./src/**/Akka.MultiNodeTestRunner.csproj"

    // Windows .NET 4.5.2
    executableProjects |> Seq.iter (fun project ->
        DotNetCli.Restore
            (fun p -> 
                { p with
                    Project = project                  
                    AdditionalArgs = ["-r win7-x64"; sprintf "/p:VersionSuffix=%s" versionSuffix] })
    )

    // Windows .NET 4.5.2
    executableProjects |> Seq.iter (fun project ->  
        DotNetCli.Publish
            (fun p ->
                { p with
                    Project = project
                    Configuration = configuration
                    Runtime = "win7-x64"
                    Framework = "net452"
                    VersionSuffix = versionSuffix }))

    // Windows .NET Core
    executableProjects |> Seq.iter (fun project ->  
        DotNetCli.Publish
            (fun p ->
                { p with
                    Project = project
                    Configuration = configuration
                    Runtime = "win7-x64"
                    Framework = "netcoreapp1.1"
                    VersionSuffix = versionSuffix }))
)

Target "CreateMntrNuget" (fun _ -> 
    // uses the template file to create a temporary .nuspec file with the correct version
    CopyFile "./src/core/Akka.MultiNodeTestRunner/Akka.MultiNodeTestRunner.nuspec" "./src/core/Akka.MultiNodeTestRunner/Akka.MultiNodeTestRunner.nuspec.template"
    let commonPropsVersionPrefix = XMLRead true "./src/common.props" "" "" "//Project/PropertyGroup/VersionPrefix" |> Seq.head
    let versionReplacement = List.ofSeq [ "@version@", commonPropsVersionPrefix + (if (not (versionSuffix = "")) then ("-" + versionSuffix) else "") ]
    TemplateHelper.processTemplates versionReplacement [ "./src/core/Akka.MultiNodeTestRunner/Akka.MultiNodeTestRunner.nuspec" ]

    let executableProjects = !! "./src/**/Akka.MultiNodeTestRunner.csproj"
    
    executableProjects |> Seq.iter (fun project ->  
        DotNetCli.Pack
            (fun p -> 
                { p with
                    Project = project
                    Configuration = configuration
                    AdditionalArgs = ["--include-symbols"]
                    VersionSuffix = overrideVersionSuffix project
                    OutputPath = outputNuGet } )
    )

    DeleteFile "./src/core/Akka.MultiNodeTestRunner/Akka.MultiNodeTestRunner.nuspec"
)

Target "PublishNuget" (fun _ ->
    let nugetExe = FullName @"./tools/nuget.exe"
    let rec publishPackage url accessKey trialsLeft packageFile =
        let tracing = enableProcessTracing
        enableProcessTracing <- false
        let args p =
            match p with
            | (pack, key, "") -> sprintf "push \"%s\" %s" pack key
            | (pack, key, url) -> sprintf "push \"%s\" %s -source %s" pack key url

        tracefn "Pushing %s Attempts left: %d" (FullName packageFile) trialsLeft
        try 
            let result = ExecProcess (fun info -> 
                    info.FileName <- nugetExe
                    info.WorkingDirectory <- (Path.GetDirectoryName (FullName packageFile))
                    info.Arguments <- args (packageFile, accessKey,url)) (System.TimeSpan.FromMinutes 1.0)
            enableProcessTracing <- tracing
            if result <> 0 then failwithf "Error during NuGet symbol push. %s %s" nugetExe (args (packageFile, "key omitted",url))
        with exn -> 
            if (trialsLeft > 0) then (publishPackage url accessKey (trialsLeft-1) packageFile)
            else raise exn
    let shouldPushNugetPackages = hasBuildParam "nugetkey"
    let shouldPushSymbolsPackages = (hasBuildParam "symbolspublishurl") && (hasBuildParam "symbolskey")
    
    if (shouldPushNugetPackages || shouldPushSymbolsPackages) then
        printfn "Pushing nuget packages"
        if shouldPushNugetPackages then
            let normalPackages= 
                !! (outputNuGet @@ "*.nupkg") 
                -- (outputNuGet @@ "*.symbols.nupkg") |> Seq.sortBy(fun x -> x.ToLower())
            for package in normalPackages do
                try
                    publishPackage (getBuildParamOrDefault "nugetpublishurl" "") (getBuildParam "nugetkey") 3 package
                with exn ->
                    printfn "%s" exn.Message

        if shouldPushSymbolsPackages then
            let symbolPackages= !! (outputNuGet @@ "*.symbols.nupkg") |> Seq.sortBy(fun x -> x.ToLower())
            for package in symbolPackages do
                try
                    publishPackage (getBuildParam "symbolspublishurl") (getBuildParam "symbolskey") 3 package
                with exn ->
                    printfn "%s" exn.Message
)

//--------------------------------------------------------------------------------
// Serialization
//--------------------------------------------------------------------------------
Target "Protobuf" <| fun _ ->

    let protocPath =
        if isWindows then findToolInSubPath "protoc.exe" "src/packages/Google.Protobuf.Tools/tools/windows_x64"
        elif isMacOS then findToolInSubPath "protoc" "src/packages/Google.Protobuf.Tools/tools/macosx_x64"
        else findToolInSubPath "protoc" "src/packages/Google.Protobuf.Tools/tools/linux_x64"

    let protoFiles = [
        ("WireFormats.proto", "/src/core/Akka.Remote/Serialization/Proto/");
        ("ContainerFormats.proto", "/src/core/Akka.Remote/Serialization/Proto/");
        ("ContainerFormats.proto", "/src/core/Akka.Remote/Serialization/Proto/");
        ("SystemMessageFormats.proto", "/src/core/Akka.Remote/Serialization/Proto/");
        ("ClusterMessages.proto", "/src/core/Akka.Cluster/Serialization/Proto/");
        ("ClusterClientMessages.proto", "/src/contrib/cluster/Akka.Cluster.Tools/Client/Serialization/Proto/");
        ("DistributedPubSubMessages.proto", "/src/contrib/cluster/Akka.Cluster.Tools/PublishSubscribe/Serialization/Proto/");
        ("ClusterShardingMessages.proto", "/src/contrib/cluster/Akka.Cluster.Sharding/Serialization/Proto/");
        ("TestConductorProtocol.proto", "/src/core/Akka.Remote.TestKit/Proto/");
        ("Persistence.proto", "/src/core/Akka.Persistence/Serialization/Proto/") ]

    printfn "Using proto.exe: %s" protocPath

    let runProtobuf assembly =
        let protoName, destinationPath = assembly
        let args = StringBuilder()
                |> append (sprintf "-I=%s" (__SOURCE_DIRECTORY__ @@ "/src/protobuf/") )
                |> append (sprintf "-I=%s" (__SOURCE_DIRECTORY__ @@ "/src/protobuf/common") )
                |> append (sprintf "--csharp_out=internal_access:%s" (__SOURCE_DIRECTORY__ @@ destinationPath))
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
Target "DocFx" (fun _ ->
    // build the project with samples
    let docsExamplesSolution = "./docs/examples/DocsExamples.sln"
    DotNetCli.Restore (fun p -> { p with Project = docsExamplesSolution })
    DotNetCli.Build (fun p -> { p with Project = docsExamplesSolution; Configuration = configuration })

    // install MSDN references
    NugetInstall (fun p -> 
            { p with
                ExcludeVersion = true
                Version = "0.1.0-alpha-1611021200"
                OutputDirectory = currentDirectory @@ "tools" }) "msdn.4.5.2"

    let docsPath = "./docs"
    DocFx (fun p -> 
                { p with 
                    Timeout = TimeSpan.FromMinutes 30.0; 
                    WorkingDirectory  = docsPath; 
                    DocFxJson = docsPath @@ "docfx.json" })
)

FinalTarget "KillCreatedProcesses" (fun _ ->
    log "Killing processes started by FAKE:"
    startedProcesses |> Seq.iter (fun (pid, _) -> logfn "%i" pid)
    killAllCreatedProcesses()
    log "Killing any remaining dotnet and xunit.console.exe processes:"
    getProcessesByName "dotnet" |> Seq.iter (fun p -> logfn "pid: %i; name: %s" p.Id p.ProcessName)
    killProcess "dotnet"
    getProcessesByName "xunit.console" |> Seq.iter (fun p -> logfn "pid: %i; name: %s" p.Id p.ProcessName)
    killProcess "xunit.console"
)

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
"Clean" ==> "RestorePackages" ==> "AssemblyInfo" ==> "Build" ==> "PublishMntr" ==> "BuildRelease"

// tests dependencies
// "RunTests" step doesn't require Clean ==> "RestorePackages" step
"Clean" ==> "RestorePackages" ==> "RunTestsNetCore"

// nuget dependencies
"BuildRelease" ==> "CreateMntrNuget" ==> "CreateNuget" ==> "PublishNuget" ==> "Nuget"

// docs
"BuildRelease" ==> "Docfx"

// all
"BuildRelease" ==> "All"
"RunTests" ==> "All"
"RunTestsNetCore" ==> "All"
"MultiNodeTests" ==> "All"
"NBench" ==> "All"

RunTargetOrDefault "Help"
