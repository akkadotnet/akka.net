#I @"src/packages/FAKE/tools"
#r "FakeLib.dll"
#r "System.Xml.Linq"

open System
open System.IO
open System.Text
open Fake
open Fake.FileUtils
open Fake.TaskRunnerHelper
open Fake.ProcessHelper

cd __SOURCE_DIRECTORY__

//--------------------------------------------------------------------------------
// Information about the project for Nuget and Assembly info files
//--------------------------------------------------------------------------------


let product = "Akka.NET"
let authors = [ "Akka.NET Team" ]
let copyright = "Copyright © 2013-2016 Akka.NET Team"
let company = "Akka.NET Team"
let description = "Akka.NET is a port of the popular Java/Scala framework Akka to .NET"
let tags = ["akka";"actors";"actor";"model";"Akka";"concurrency"]
let configuration = "Release"
let toolDir = "tools"
let CloudCopyDir = toolDir @@ "CloudCopy"
let AzCopyDir = toolDir @@ "AzCopy"

// Read release notes and version

let parsedRelease =
    File.ReadLines "RELEASE_NOTES.md"
    |> ReleaseNotesHelper.parseReleaseNotes

let envBuildNumber = System.Environment.GetEnvironmentVariable("BUILD_NUMBER")
let buildNumber = if String.IsNullOrWhiteSpace(envBuildNumber) then "0" else envBuildNumber

let version = parsedRelease.AssemblyVersion + "." + buildNumber
let preReleaseVersion = version + "-beta"

let isUnstableDocs = hasBuildParam "unstable"
let isPreRelease = hasBuildParam "nugetprerelease"
let release = if isPreRelease then ReleaseNotesHelper.ReleaseNotes.New(version, version + "-beta", parsedRelease.Notes) else parsedRelease

printfn "Assembly version: %s\nNuget version; %s\n" release.AssemblyVersion release.NugetVersion
//--------------------------------------------------------------------------------
// Directories

let binDir = "bin"
let testOutput = FullName "TestResults"
let perfOutput = FullName "PerfResults"

let nugetDir = binDir @@ "nuget"
let workingDir = binDir @@ "build"
let libDir = workingDir @@ @"lib\net45\"
let nugetExe = FullName @"src\.nuget\NuGet.exe"
let docDir = "bin" @@ "doc"
let sourceBrowserDocsDir = binDir @@ "sourcebrowser"
let msdeployPath = "C:\Program Files (x86)\IIS\Microsoft Web Deploy V3\msdeploy.exe"

open Fake.RestorePackageHelper
Target "RestorePackages" (fun _ -> 
     "./src/Akka.sln"
     |> RestoreMSSolutionPackages (fun p ->
         { p with
             OutputPath = "./src/packages"
             Retries = 4 })
 )

//--------------------------------------------------------------------------------
// Clean build results

Target "Clean" <| fun _ ->
    DeleteDir binDir

//--------------------------------------------------------------------------------
// Generate AssemblyInfo files with the version for release notes 

open AssemblyInfoFile

Target "AssemblyInfo" <| fun _ ->
    CreateCSharpAssemblyInfoWithConfig "src/SharedAssemblyInfo.cs" [
        Attribute.Company company
        Attribute.Copyright copyright
        Attribute.Trademark ""
        Attribute.Version version
        Attribute.FileVersion version ] <| AssemblyInfoFileConfig(false)

    for file in !! "src/**/AssemblyInfo.fs" do
        let title =
            file
            |> Path.GetDirectoryName
            |> Path.GetDirectoryName
            |> Path.GetFileName

        CreateFSharpAssemblyInfo file [ 
            Attribute.Title title
            Attribute.Product product
            Attribute.Description description
            Attribute.Copyright copyright
            Attribute.Company company
            Attribute.ComVisible false
            Attribute.CLSCompliant true
            Attribute.Version version
            Attribute.FileVersion version ]


//--------------------------------------------------------------------------------
// Build the solution

Target "Build" <| fun _ ->

    !!"src/Akka.sln"
    |> MSBuildRelease "" "Rebuild"
    |> ignore

Target "BuildMono" <| fun _ ->

    !!"src/Akka.sln"
    |> MSBuild "" "Rebuild" [("Configuration","Release Mono")]
    |> ignore

//--------------------------------------------------------------------------------
// Build the docs
Target "Docs" <| fun _ ->
    !! "documentation/akkadoc.shfbproj"
    |> MSBuildRelease "" "Rebuild"
    |> ignore

//--------------------------------------------------------------------------------
// Push DOCs content to Windows Azure blob storage
Target "AzureDocsDeploy" (fun _ ->
    let rec pushToAzure docDir azureUrl container azureKey trialsLeft =
        let tracing = enableProcessTracing
        enableProcessTracing <- false
        let arguments = sprintf "/Source:%s /Dest:%s /DestKey:%s /S /Y /SetContentType" (Path.GetFullPath docDir) (azureUrl @@ container) azureKey
        tracefn "Pushing docs to %s. Attempts left: %d" (azureUrl) trialsLeft
        try 
            
            let result = ExecProcess(fun info ->
                info.FileName <- AzCopyDir @@ "AzCopy.exe"
                info.Arguments <- arguments) (TimeSpan.FromMinutes 120.0) //takes a very long time to upload
            if result <> 0 then failwithf "Error during AzCopy.exe upload to azure."
        with exn -> 
            if (trialsLeft > 0) then (pushToAzure docDir azureUrl container azureKey (trialsLeft-1))
            else raise exn
    let canPush = hasBuildParam "azureKey" && hasBuildParam "azureUrl"
    if (canPush) then
         printfn "Uploading API docs to Azure..."
         let azureUrl = getBuildParam "azureUrl"
         let azureKey = (getBuildParam "azureKey") + "==" //hack, because it looks like FAKE arg parsing chops off the "==" that gets tacked onto the end of each Azure storage key
         if(isUnstableDocs) then
            pushToAzure docDir azureUrl "unstable" azureKey 3
         if(not isUnstableDocs) then
            pushToAzure docDir azureUrl "stable" azureKey 3
            pushToAzure docDir azureUrl release.NugetVersion azureKey 3
    if(not canPush) then
        printfn "Missing required paraments to push docs to Azure. Run build HelpDocs to find out!"
            
)

Target "PublishDocs" DoNothing

//--------------------------------------------------------------------------------
// Build the SourceBrowser docs
//--------------------------------------------------------------------------------
Target "GenerateSourceBrowser" <| (fun _ ->
    DeleteDir sourceBrowserDocsDir

    let htmlGeneratorPath = "src/packages/Microsoft.SourceBrowser/tools/HtmlGenerator.exe"
    let arguments = sprintf "/out:%s %s" sourceBrowserDocsDir "src/Akka.sln"
    printfn "Using SourceBrowser: %s %s" htmlGeneratorPath arguments
    
    let result = ExecProcess(fun info -> 
        info.FileName <- htmlGeneratorPath
        info.Arguments <- arguments) (System.TimeSpan.FromMinutes 20.0)
    
    if result <> 0 then failwithf "SourceBrowser failed. %s %s" htmlGeneratorPath arguments
)

//--------------------------------------------------------------------------------
// Publish the SourceBrowser docs
//--------------------------------------------------------------------------------
Target "PublishSourceBrowser" <| (fun _ ->
    let canPublish = hasBuildParam "publishsettings"
    if (canPublish) then
        let sourceBrowserPublishSettingsPath = getBuildParam "publishsettings"
        let arguments = sprintf "-verb:sync -source:contentPath=\"%s\" -dest:contentPath=sourcebrowser,publishSettings=\"%s\"" (Path.GetFullPath sourceBrowserDocsDir) sourceBrowserPublishSettingsPath
        printfn "Using MSDeploy: %s %s" msdeployPath arguments
    
        let result = ExecProcess(fun info -> 
            info.FileName <- msdeployPath
            info.Arguments <- arguments) (System.TimeSpan.FromMinutes 30.0) //takes a long time to upload
    
        if result <> 0 then failwithf "MSDeploy failed. %s %s" msdeployPath arguments
    else
        printfn "Missing required parameter to publish SourceBrowser docs. Run build HelpSourceBrowserDocs to find out!"
)

//--------------------------------------------------------------------------------
// Copy the build output to bin directory
//--------------------------------------------------------------------------------

Target "CopyOutput" <| fun _ ->
    
    let copyOutput project =
        let src = "src" @@ project @@ @"bin/Release/"
        let dst = binDir @@ project
        CopyDir dst src allFiles
    [ "core/Akka"
      "core/Akka.FSharp"
      "core/Akka.TestKit"
      "core/Akka.Remote"
      "core/Akka.Remote.TestKit"
      "core/Akka.Cluster"
      "core/Akka.Cluster.TestKit"
      "core/Akka.MultiNodeTestRunner"
      "core/Akka.Persistence"
      "core/Akka.Persistence.FSharp"
      "core/Akka.Persistence.TestKit"
      "core/Akka.Persistence.Query"
      "core/Akka.Streams"
      "core/Akka.Streams.TestKit"
      "contrib/dependencyinjection/Akka.DI.Core"
      "contrib/dependencyinjection/Akka.DI.TestKit"
      "contrib/testkits/Akka.TestKit.Xunit" 
      "contrib/testkits/Akka.TestKit.Xunit2" 
      "contrib/serializers/Akka.Serialization.Wire" 
      "contrib/cluster/Akka.Cluster.Tools"
      "contrib/cluster/Akka.Cluster.Sharding"
      ]
    |> List.iter copyOutput

Target "BuildRelease" DoNothing



//--------------------------------------------------------------------------------
// Tests targets
//--------------------------------------------------------------------------------

//--------------------------------------------------------------------------------
// Clean test output

Target "CleanTests" <| fun _ ->
    DeleteDir testOutput
//--------------------------------------------------------------------------------
// Run tests

open Fake.Testing
Target "RunTests" <| fun _ ->  
    let xunitTestAssemblies = !! "src/**/bin/Release/*.Tests.dll" -- 
                                 // Akka.Streams.Tests is referencing Akka.Streams.TestKit.Tests
                                 "src/**/Akka.Streams.Tests/bin/Release/Akka.Streams.TestKit.Tests.dll" --
                                 // Akka.Streams.Tests.Performance is referencing Akka.Streams.Tests and Akka.Streams.TestKit.Tests
                                 "src/**/Akka.Streams.Tests.Performance/bin/Release/*.Tests.dll"

    let nunitTestAssemblies = !! "src/**/bin/Release/Akka.Streams.Tests.TCK.dll"

    mkdir testOutput
   
    let xunitToolPath = findToolInSubPath "xunit.console.exe" "src/packages/FAKE/xunit.runner.console*/tools"
    let nunitToolPath = findToolInSubPath "nunit3-console.exe" "src/packages/FAKE/NUnit.ConsoleRunner/tools"

    printfn "Using XUnit runner: %s" xunitToolPath
    let runSingleAssembly assembly =
        let assemblyName = Path.GetFileNameWithoutExtension(assembly)
        xUnit2
            (fun p -> { p with XmlOutputPath = Some (testOutput + @"\" + assemblyName + "_xunit.xml"); HtmlOutputPath = Some (testOutput + @"\" + assemblyName + "_xunit.HTML"); ToolPath = xunitToolPath; TimeOut = System.TimeSpan.FromMinutes 30.0; Parallel = ParallelMode.NoParallelization }) 
            (Seq.singleton assembly)

    xunitTestAssemblies |> Seq.iter (runSingleAssembly)
    
    let runNunitSingleAssembly assembly = 
        let assemblyName = Path.GetFileNameWithoutExtension(assembly)
        NUnit3
             (fun p -> { p with ToolPath = nunitToolPath; WorkingDir = testOutput})
             (Seq.singleton assembly)
        
    printfn "Using NUnit runner: %s" nunitToolPath
    nunitTestAssemblies |> Seq.iter (runNunitSingleAssembly)

Target "RunTestsMono" <| fun _ ->  
    let xunitTestAssemblies = !! "src/**/bin/Release Mono/*.Tests.dll"

    mkdir testOutput

    let xunitToolPath = findToolInSubPath "xunit.console.exe" "src/packages/xunit.runner.console*/tools"
    printfn "Using XUnit runner: %s" xunitToolPath
    let runSingleAssembly assembly =
        let assemblyName = Path.GetFileNameWithoutExtension(assembly)
        xUnit2
            (fun p -> { p with XmlOutputPath = Some (testOutput + @"\" + assemblyName + "_xunit.xml"); HtmlOutputPath = Some (testOutput + @"\" + assemblyName + "_xunit.HTML"); ToolPath = xunitToolPath; TimeOut = System.TimeSpan.FromMinutes 30.0; Parallel = ParallelMode.NoParallelization }) 
            (Seq.singleton assembly)

    xunitTestAssemblies |> Seq.iter (runSingleAssembly)


(* Debug helper for troubleshooting an issue we had when we were running multi-node tests multiple times *)
Target "PrintMultiNodeTests" <| fun _ ->
    let testSearchPath =
        let assemblyFilter = getBuildParamOrDefault "spec-assembly" String.Empty
        sprintf "src/**/bin/Release/*%s*.Tests.MultiNode.dll" assemblyFilter
    (!! testSearchPath) |> Seq.iter (printfn "%s")
    


Target "MultiNodeTests" <| fun _ ->
    let testSearchPath =
        let assemblyFilter = getBuildParamOrDefault "spec-assembly" String.Empty
        sprintf "src/**/bin/Release/*%s*.Tests.MultiNode.dll" assemblyFilter

    mkdir testOutput
    let multiNodeTestPath = findToolInSubPath "Akka.MultiNodeTestRunner.exe" "bin/core/Akka.MultiNodeTestRunner*"
    let multiNodeTestAssemblies = !! testSearchPath
    printfn "Using MultiNodeTestRunner: %s" multiNodeTestPath

    let runMultiNodeSpec assembly =
        let spec = getBuildParam "spec"

        let args = new StringBuilder()
                |> append assembly
                |> append "-Dmultinode.enable-filesink=on"
                |> append (sprintf "-Dmultinode.output-directory=\"%s\"" testOutput)
                |> appendIfNotNullOrEmpty spec "-Dmultinode.test-spec="
                |> toText

        let result = ExecProcess(fun info -> 
            info.FileName <- multiNodeTestPath
            info.WorkingDirectory <- (Path.GetDirectoryName (FullName multiNodeTestPath))
            info.Arguments <- args) (System.TimeSpan.FromMinutes 60.0) (* This is a VERY long running task. *)
        if result <> 0 then failwithf "MultiNodeTestRunner failed. %s %s" multiNodeTestPath args
    
    multiNodeTestAssemblies |> Seq.iter (runMultiNodeSpec)

//--------------------------------------------------------------------------------
// NBench targets 
//--------------------------------------------------------------------------------
Target "NBench" <| fun _ ->
    let testSearchPath =
        let assemblyFilter = getBuildParamOrDefault "spec-assembly" String.Empty
        sprintf "src/**/bin/Release/*%s*.Tests.Performance.dll" assemblyFilter

    mkdir perfOutput
    let nbenchTestPath = findToolInSubPath "NBench.Runner.exe" "src/packges/NBench.Runner*"
    let nbenchTestAssemblies = !! testSearchPath
    printfn "Using NBench.Runner: %s" nbenchTestPath

    let runNBench assembly =
        let spec = getBuildParam "spec"

        let args = new StringBuilder()
                |> append assembly
                |> append (sprintf "output-directory=\"%s\"" perfOutput)
                |> append (sprintf "concurrent=\"%b\"" true)
                |> append (sprintf "trace=\"%b\"" true)
                |> toText

        let result = ExecProcess(fun info -> 
            info.FileName <- nbenchTestPath
            info.WorkingDirectory <- (Path.GetDirectoryName (FullName nbenchTestPath))
            info.Arguments <- args) (System.TimeSpan.FromMinutes 15.0) (* Reasonably long-running task. *)
        if result <> 0 then failwithf "NBench.Runner failed. %s %s" nbenchTestPath args
    
    nbenchTestAssemblies |> Seq.iter (runNBench)

//--------------------------------------------------------------------------------
// Clean NBench output
Target "CleanPerf" <| fun _ ->
    DeleteDir perfOutput


//--------------------------------------------------------------------------------
// Nuget targets 
//--------------------------------------------------------------------------------

module Nuget = 
    // add Akka dependency for other projects
    let getAkkaDependency project =
        match project with
        | "Akka" -> []
        | "Akka.Cluster" -> ["Akka.Remote", release.NugetVersion]
        | "Akka.Cluster.TestKit" -> ["Akka.Remote.TestKit", release.NugetVersion; "Akka.Cluster", release.NugetVersion]
        | "Akka.Cluster.Sharding" -> ["Akka.Cluster.Tools", preReleaseVersion; "Akka.Persistence", preReleaseVersion]
        | "Akka.Cluster.Tools" -> ["Akka.Cluster", release.NugetVersion]
        | "Akka.MultiNodeTestRunner" -> [] // all binaries for the multinodetest runner have to be included locally
        | "Akka.Persistence.TestKit" -> ["Akka.Persistence", preReleaseVersion; "Akka.TestKit.Xunit2", release.NugetVersion]
        | "Akka.Persistence.Query" -> ["Akka.Persistence", preReleaseVersion; "Akka.Streams", preReleaseVersion]
        | "Akka.Persistence.Query.Sql" -> ["Akka.Persistence.Query", preReleaseVersion; "Akka.Persistence.Sql.Common", preReleaseVersion]
        | "Akka.Persistence.Sql.TestKit" -> ["Akka.Persistence.Query.Sql", preReleaseVersion; "Akka.Persistence.TestKit", preReleaseVersion; "Akka.Streams.TestKit", preReleaseVersion]
        | persistence when (persistence.Contains("Sql") && not (persistence.Equals("Akka.Persistence.Sql.Common"))) -> ["Akka.Persistence.Sql.Common", preReleaseVersion]
        | persistence when (persistence.StartsWith("Akka.Persistence.")) -> ["Akka.Persistence", preReleaseVersion]
        | "Akka.DI.TestKit" -> ["Akka.DI.Core", release.NugetVersion; "Akka.TestKit.Xunit2", release.NugetVersion]
        | testkit when testkit.StartsWith("Akka.TestKit.") -> ["Akka.TestKit", release.NugetVersion]
        | "Akka.Remote.TestKit" -> ["Akka.Remote", release.NugetVersion; "Akka.TestKit.Xunit2", release.NugetVersion;]
        | "Akka.Streams" -> ["Akka", release.NugetVersion]
        | "Akka.Streams.TestKit" -> ["Akka.Streams", preReleaseVersion; "Akka.TestKit", release.NugetVersion]
        | _ -> ["Akka", release.NugetVersion]

    // used to add -pre suffix to pre-release packages
    let getProjectVersion project =
      match project with
      | "Akka.Serialization.Wire" -> preReleaseVersion
      | cluster when (cluster.StartsWith("Akka.Cluster.") && not (cluster.EndsWith("TestKit"))) -> preReleaseVersion
      | persistence when persistence.StartsWith("Akka.Persistence") -> preReleaseVersion
      | streams when streams.StartsWith("Akka.Streams") -> preReleaseVersion
      | _ -> release.NugetVersion

open Nuget

//--------------------------------------------------------------------------------
// Clean nuget directory

Target "CleanNuget" <| fun _ ->
    CleanDir nugetDir

//--------------------------------------------------------------------------------
// Pack nuget for all projects
// Publish to nuget.org if nugetkey is specified

let createNugetPackages _ =
    let removeDir dir = 
        let del _ = 
            DeleteDir dir
            not (directoryExists dir)
        runWithRetries del 3 |> ignore

    let getReleaseFiles project releaseDir =
        match project with
        | "Akka.MultiNodeTestRunner" -> // because the MNTR is an exe, all of its dlls have to be available in the same working directory when it executes
            !! (releaseDir @@ "*.dll")
            ++ (releaseDir @@ "*.exe")
            ++ (releaseDir @@ "*.pdb")
            ++ (releaseDir @@ "*.xml")
        | _ ->
            !! (releaseDir @@ project + ".dll")
            ++ (releaseDir @@ project + ".exe")
            ++ (releaseDir @@ project + ".pdb")
            ++ (releaseDir @@ project + ".xml")

    let getExternalPackages project packagesFile =
        match project with
        | "Akka.MultiNodeTestRunner" -> [] // because the MNTR is an exe, all of its dlls have to be available in the same working directory when it executes
        | _ -> if (fileExists packagesFile) then (getDependencies packagesFile) else []

    ensureDirectory nugetDir
    for nuspec in !! "src/**/*.nuspec" do
        printfn "Creating nuget packages for %s" nuspec
        
        CleanDir workingDir

        let project = Path.GetFileNameWithoutExtension nuspec 
        let projectDir = Path.GetDirectoryName nuspec
        let projectFile = (!! (projectDir @@ project + ".*sproj")) |> Seq.head
        let releaseDir = projectDir @@ @"bin\Release"
        let packages = projectDir @@ "packages.config"
        let packageDependencies = getExternalPackages project packages
        let dependencies = packageDependencies @ getAkkaDependency project
        let releaseVersion = getProjectVersion project

        let pack outputDir symbolPackage =
            NuGetHelper.NuGet
                (fun p ->
                    { p with
                        Description = description
                        Authors = authors
                        Copyright = copyright
                        Project =  project
                        Properties = ["Configuration", "Release"]
                        ReleaseNotes = release.Notes |> String.concat "\n"
                        Version = releaseVersion
                        Tags = tags |> String.concat " "
                        OutputPath = outputDir
                        WorkingDir = workingDir
                        SymbolPackage = symbolPackage
                        Dependencies = dependencies })
                nuspec

        // Copy dll, pdb and xml to libdir = workingDir/lib/net45/
        ensureDirectory libDir
        getReleaseFiles project releaseDir
        |> CopyFiles libDir

        // Copy all src-files (.cs and .fs files) to workingDir/src
        let nugetSrcDir = workingDir @@ @"src/"
        // CreateDir nugetSrcDir

        let isCs = hasExt ".cs"
        let isFs = hasExt ".fs"
        let isAssemblyInfo f = (filename f).Contains("AssemblyInfo")
        let isSrc f = (isCs f || isFs f) && not (isAssemblyInfo f) 
        CopyDir nugetSrcDir projectDir isSrc
        
        //Remove workingDir/src/obj and workingDir/src/bin
        removeDir (nugetSrcDir @@ "obj")
        removeDir (nugetSrcDir @@ "bin")

        // Create both normal nuget package and symbols nuget package. 
        // Uses the files we copied to workingDir and outputs to nugetdir
        pack nugetDir NugetSymbolPackage.Nuspec
        
        removeDir workingDir

let publishNugetPackages _ = 
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
                !! (nugetDir @@ "*.nupkg") 
                -- (nugetDir @@ "*.symbols.nupkg") |> Seq.sortBy(fun x -> x.ToLower())
            for package in normalPackages do
                try
                    publishPackage (getBuildParamOrDefault "nugetpublishurl" "") (getBuildParam "nugetkey") 3 package
                with exn ->
                    printfn "%s" exn.Message

        if shouldPushSymbolsPackages then
            let symbolPackages= !! (nugetDir @@ "*.symbols.nupkg") |> Seq.sortBy(fun x -> x.ToLower())
            for package in symbolPackages do
                try
                    publishPackage (getBuildParam "symbolspublishurl") (getBuildParam "symbolskey") 3 package
                with exn ->
                    printfn "%s" exn.Message


Target "Nuget" <| fun _ -> 
    createNugetPackages()
    publishNugetPackages()

Target "CreateNuget" <| fun _ -> 
    createNugetPackages()

Target "PublishNuget" <| fun _ -> 
    publishNugetPackages()



//--------------------------------------------------------------------------------
// Help 
//--------------------------------------------------------------------------------

Target "Help" <| fun _ ->
    List.iter printfn [
      "usage:"
      "build [target]"
      ""
      " Targets for building:"
      " * Build      Builds"
      " * Nuget      Create and optionally publish nugets packages"
      " * RunTests   Runs tests"
      " * MultiNodeTests  Runs the slower multiple node specifications"
      " * All        Builds, run tests, creates and optionally publish nuget packages"
      ""
      " Other Targets"
      " * Help       Display this help" 
      " * HelpNuget  Display help about creating and pushing nuget packages" 
      " * HelpDocs   Display help about creating and pushing API docs"
      " * HelpMultiNodeTests  Display help about running the multiple node specifications"
      ""]

Target "HelpNuget" <| fun _ ->
    List.iter printfn [
      "usage: "
      "build Nuget [nugetkey=<key> [nugetpublishurl=<url>]] "
      "            [symbolskey=<key> symbolspublishurl=<url>] "
      "            [nugetprerelease=<prefix>]"
      ""
      "Arguments for Nuget target:"
      "   nugetprerelease=<prefix>   Creates a pre-release package."
      "                              The version will be version-prefix<date>"
      "                              Example: nugetprerelease=dev =>"
      "                                       0.6.3-dev1408191917"
      ""
      "In order to publish a nuget package, keys must be specified."
      "If a key is not specified the nuget packages will only be created on disk"
      "After a build you can find them in bin/nuget"
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
      "  build Nuget                      Build nuget packages to the bin/nuget folder"
      ""
      "  build Nuget nugetprerelease=dev  Build pre-release nuget packages"
      ""
      "  build Nuget nugetkey=123         Build and publish to nuget.org and symbolsource.org"
      ""
      "  build Nuget nugetprerelease=dev nugetkey=123 nugetpublishurl=http://abc"
      "              symbolskey=456 symbolspublishurl=http://xyz"
      "                                   Build and publish pre-release nuget packages to http://abc"
      "                                   and symbols packages to http://xyz"
      ""]

Target "HelpDocs" <| fun _ ->
    List.iter printfn [
      "usage: "
      "build Docs"
      "Just builds the API docs for Akka.NET locally. Does not attempt to publish."
      ""
      "build PublishDocs azureKey=<key> "
      "                  azureUrl=<url> "
      "                 [unstable=true]"
      ""
      "Arguments for PublishDocs target:"
      "   azureKey=<key>             Azure blob storage key."
      "                              Used to authenticate to the storage account."
      ""
      "   azureUrl=<url>             Base URL for Azure storage container."
      "                              FAKE will automatically set container"
      "                              names based on build parameters."
      ""
      "   [unstable=true]            Indicates that we'll publish to an Azure"
      "                              container named 'unstable'. If this param"
      "                              is not present we'll publish to containers"
      "                              'stable' and the 'release.version'"
      ""
      "In order to publish documentation all of these values must be provided."
      "Examples:"
      "  build PublishDocs azureKey=1s9HSAHA+..."
      "                    azureUrl=http://fooaccount.blob.core.windows.net/docs"
      "                                   Build and publish docs to http://fooaccount.blob.core.windows.net/docs/stable"
      "                                   and http://fooaccount.blob.core.windows.net/docs/{release.version}"
      ""
      "  build PublishDocs azureKey=1s9HSAHA+..."
      "                    azureUrl=http://fooaccount.blob.core.windows.net/docs"
      "                    unstable=true"
      "                                   Build and publish docs to http://fooaccount.blob.core.windows.net/docs/unstable"
      ""]


Target "HelpSourceBrowserDocs" <| fun _ ->
    List.iter printfn [
      "usage: "
      "build GenerateSourceBrowser"
      "Just generates the SourceBrowser docs for Akka.NET locally. Does not attempt to publish."
      ""
      "build PublishSourceBrowser publishsettings=<filePath> "
      ""
      "Arguments for PublishSourceBrowser target:"
      "   publishsettings=<filePath> Publish settings file."
      ""
      "In order to publish documentation all of these values must be provided."
      ""]


Target "HelpMultiNodeTests" <| fun _ ->
    List.iter printfn [
      "usage: "
      "build MultiNodeTests [spec-assembly=<filter>]"
      "Just runs the MultiNodeTests. Does not build the projects."
      ""
      "Arguments for MultiNodeTests target:"
      "   [spec-assembly=<filter>]  Restrict which spec projects are run."
      ""
      "       Alters the discovery filter to enable restricting which specs are run."
      "       If not supplied the filter used is '*.Tests.Multinode.Dll'"
      "       When supplied this is altered to '*<filter>*.Tests.Multinode.Dll'"
      ""]
//--------------------------------------------------------------------------------
//  Target dependencies
//--------------------------------------------------------------------------------

// build dependencies
"Clean" ==> "AssemblyInfo" ==> "RestorePackages" ==> "Build" ==> "CopyOutput" ==> "BuildRelease"

// tests dependencies
"CleanTests" ==> "RunTests"
"CleanTests" ==> "MultiNodeTests"

// NBench dependencies
"CleanPerf" ==> "NBench"

// nuget dependencies
"CleanNuget" ==> "CreateNuget"
"CleanNuget" ==> "BuildRelease" ==> "Nuget"

//docs dependencies
"BuildRelease" ==> "Docs" ==> "AzureDocsDeploy" ==> "PublishDocs"

// SourceBrowser dependencies
"BuildRelease" ==> "GenerateSourceBrowser" ==> "PublishSourceBrowser"

Target "All" DoNothing
"BuildRelease" ==> "All"
"RunTests" ==> "All"
"MultiNodeTests" ==> "All"
"NBench" ==> "All"
"Nuget" ==> "All"

Target "AllTests" DoNothing //used for Mono builds, due to Mono 4.0 bug with FAKE / NuGet https://github.com/fsharp/fsharp/issues/427
"BuildRelease" ==> "AllTests"
"RunTests" ==> "AllTests"
"MultiNodeTests" ==> "AllTests"

RunTargetOrDefault "Help"
