#I @"src/packages/FAKE/tools"
#r "FakeLib.dll"
#r "System.Xml.Linq"

open System
open System.IO
open Fake
open Fake.FileUtils
open Fake.MSTest
open Fake.NUnitCommon
open Fake.TaskRunnerHelper
open Fake.ProcessHelper

cd __SOURCE_DIRECTORY__

//--------------------------------------------------------------------------------
// Information about the project for Nuget and Assembly info files
//--------------------------------------------------------------------------------


let product = "Akka.NET"
let authors = [ "Akka.NET Team" ]
let copyright = "Copyright © 2013-2015 Akka.NET Team"
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

//Fake.ReleaseNotesHelper.Parse assumes letters+int in PreRelease.TryParse. 
//This means we cannot append the full date yyyMMddHHmmss to prerelease. 
//See https://github.com/fsharp/FAKE/issues/522
//TODO: When this has been fixed, switch to DateTime.UtcNow.ToString("yyyyMMddHHmmss")
let preReleaseVersion = parsedRelease.AssemblyVersion + "-" + (getBuildParamOrDefault "nugetprerelease" "pre") + DateTime.UtcNow.ToString("yyMMddHHmm")
let isUnstableDocs = hasBuildParam "unstable"
let isPreRelease = hasBuildParam "nugetprerelease"
let release = if isPreRelease then ReleaseNotesHelper.ReleaseNotes.New(parsedRelease.AssemblyVersion, preReleaseVersion, parsedRelease.Notes) else parsedRelease

printfn "Assembly version: %s\nNuget version; %s\n" release.AssemblyVersion release.NugetVersion
//--------------------------------------------------------------------------------
// Directories

let binDir = "bin"
let testOutput = "TestResults"

let nugetDir = binDir @@ "nuget"
let workingDir = binDir @@ "build"
let libDir = workingDir @@ @"lib\net45\"
let nugetExe = FullName @"src\.nuget\NuGet.exe"
let docDir = "bin" @@ "doc"


//--------------------------------------------------------------------------------
// Clean build results

Target "Clean" <| fun _ ->
    DeleteDir binDir

//--------------------------------------------------------------------------------
// Generate AssemblyInfo files with the version for release notes 


open AssemblyInfoFile
Target "AssemblyInfo" <| fun _ ->
    for file in !! "src/**/AssemblyInfo.fs" do
        let title =
            file
            |> Path.GetDirectoryName
            |> Path.GetDirectoryName
            |> Path.GetFileName
        
        let version = release.AssemblyVersion + ".0"

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

        CreateCSharpAssemblyInfoWithConfig "src/SharedAssemblyInfo.cs" [
            Attribute.Company company
            Attribute.Copyright copyright
            Attribute.Trademark ""
            Attribute.Version version
            Attribute.FileVersion version ] |> ignore

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
      "core/Akka.MultiNodeTestRunner"
      "core/Akka.Persistence"
      "core/Akka.Persistence.FSharp"
      "core/Akka.Persistence.TestKit"
      "contrib/loggers/Akka.Logger.slf4net"
      "contrib/loggers/Akka.Logger.NLog" 
      "contrib/loggers/Akka.Logger.Serilog" 
      "contrib/dependencyinjection/Akka.DI.Core"
      "contrib/dependencyinjection/Akka.DI.AutoFac"
      "contrib/dependencyinjection/Akka.DI.CastleWindsor"
      "contrib/dependencyinjection/Akka.DI.Ninject"
      "contrib/testkits/Akka.TestKit.Xunit" 
      "contrib/testkits/Akka.TestKit.NUnit" 
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

open XUnitHelper
Target "RunTests" <| fun _ ->  
    let msTestAssemblies = !! "src/**/bin/Release/Akka.TestKit.VsTest.Tests.dll"
    let nunitTestAssemblies = !! "src/**/bin/Release/Akka.TestKit.NUnit.Tests.dll"
    let xunitTestAssemblies = !! "src/**/bin/Release/*.Tests.dll" -- 
                                    "src/**/bin/Release/Akka.TestKit.VsTest.Tests.dll" -- 
                                    "src/**/bin/Release/Akka.TestKit.NUnit.Tests.dll" --
                                    "src/**/bin/Release/Akka.Persistence.SqlServer.Tests.dll"

    mkdir testOutput

    MSTest (fun p -> p) msTestAssemblies
    nunitTestAssemblies
    |> NUnit (fun p -> 
        {p with
            DisableShadowCopy = true; 
            OutputFile = testOutput + @"\NUnitTestResults.xml"})

    let xunitToolPath = findToolInSubPath "xunit.console.clr4.exe" "src/packages/xunit.runners*"
    printfn "Using XUnit runner: %s" xunitToolPath
    xUnit
        (fun p -> { p with OutputDir = testOutput; ToolPath = xunitToolPath })
        xunitTestAssemblies

Target "RunTestsMono" <| fun _ ->  
    let xunitTestAssemblies = !! "src/**/bin/Release Mono/*.Tests.dll"

    mkdir testOutput

    let xunitToolPath = findToolInSubPath "xunit.console.clr4.exe" "src/packages/xunit.runners*"
    printfn "Using XUnit runner: %s" xunitToolPath
    xUnit
        (fun p -> { p with OutputDir = testOutput; ToolPath = xunitToolPath })
        xunitTestAssemblies

Target "MultiNodeTests" <| fun _ ->
    let multiNodeTestPath = findToolInSubPath "Akka.MultiNodeTestRunner.exe" "bin/core/Akka.MultiNodeTestRunner*"
    printfn "Using MultiNodeTestRunner: %s" multiNodeTestPath


    let args = "Akka.Cluster.Tests.dll -Dmultinode.enable-filesink=on"
    let result = ExecProcess(fun info -> 
        info.FileName <- multiNodeTestPath
        info.WorkingDirectory <- (Path.GetDirectoryName (FullName multiNodeTestPath))
        info.Arguments <- args) (System.TimeSpan.FromMinutes 60.0) (* This is a VERY long running task. *)
    if result <> 0 then failwithf "MultiNodeTestRunner failed. %s %s" multiNodeTestPath args

Target "RunSqlServerTests" <| fun _ ->
    let sqlServerTests = !! "src/**/bin/Release/Akka.Persistence.SqlServer.Tests.dll"
    let xunitToolPath = findToolInSubPath "xunit.console.clr4.exe" "src/packages/xunit.runners*"
    printfn "Using XUnit runner: %s" xunitToolPath
    xUnit
        (fun p -> { p with OutputDir = testOutput; ToolPath = xunitToolPath })
        sqlServerTests

//--------------------------------------------------------------------------------
// Nuget targets 
//--------------------------------------------------------------------------------

module Nuget = 
    // add Akka dependency for other projects
    let getAkkaDependency project =
        match project with
        | "Akka" -> []
        | "Akka.Cluster" -> ["Akka.Remote", release.NugetVersion]
        | "Akka.Persistence.TestKit" -> ["Akka.Persistence", preReleaseVersion]
        | "Akka.Persistence.FSharp" -> ["Akka.Persistence", preReleaseVersion]
        | di when (di.StartsWith("Akka.DI.") && not (di.EndsWith("Core"))) -> ["Akka.DI.Core", release.NugetVersion]
        | testkit when testkit.StartsWith("Akka.TestKit.") -> ["Akka.TestKit", release.NugetVersion]
        | _ -> ["Akka", release.NugetVersion]

    // used to add -pre suffix to pre-release packages
    let getProjectVersion project =
      match project with
      | "Akka.Cluster" -> preReleaseVersion
      | persistence when persistence.StartsWith("Akka.Persistence") -> preReleaseVersion
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

    ensureDirectory nugetDir
    for nuspec in !! "src/**/*.nuspec" do
        printfn "Creating nuget packages for %s" nuspec
        
        CleanDir workingDir

        let project = Path.GetFileNameWithoutExtension nuspec 
        let projectDir = Path.GetDirectoryName nuspec
        let projectFile = (!! (projectDir @@ project + ".*sproj")) |> Seq.head
        let releaseDir = projectDir @@ @"bin\Release"
        let packages = projectDir @@ "packages.config"        
        let packageDependencies = if (fileExists packages) then (getDependencies packages) else []
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
        !! (releaseDir @@ project + ".dll")
        ++ (releaseDir @@ project + ".pdb")
        ++ (releaseDir @@ project + ".xml")
        ++ (releaseDir @@ project + ".ExternalAnnotations.xml")
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
            if result <> 0 then failwithf "Error during NuGet symbol push. %s %s" nugetExe (args (packageFile, accessKey,url))
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
                publishPackage (getBuildParamOrDefault "nugetpublishurl" "") (getBuildParam "nugetkey") 3 package

        if shouldPushSymbolsPackages then
            let symbolPackages= !! (nugetDir @@ "*.symbols.nupkg") |> Seq.sortBy(fun x -> x.ToLower())
            for package in symbolPackages do
                publishPackage (getBuildParam "symbolspublishurl") (getBuildParam "symbolskey") 3 package


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
      " * All        Builds, run tests, creates and optionally publish nuget packages"
      ""
      " Other Targets"
      " * Help       Display this help" 
      " * HelpNuget  Display help about creating and pushing nuget packages" 
      " * HelpDocs   Display help about creating and pushing API docs" 
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

//--------------------------------------------------------------------------------
//  Target dependencies
//--------------------------------------------------------------------------------

// build dependencies
"Clean" ==> "AssemblyInfo" ==> "Build" ==> "CopyOutput" ==> "BuildRelease"

// tests dependencies
"CleanTests" ==> "RunTests"

// nuget dependencies
"CleanNuget" ==> "CreateNuget"
"CleanNuget" ==> "BuildRelease" ==> "Nuget"

//docs dependencies
"BuildRelease" ==> "Docs" ==> "AzureDocsDeploy" ==> "PublishDocs"

Target "All" DoNothing
"BuildRelease" ==> "All"
"RunTests" ==> "All"
"BuildRelease" ==> "MultiNodeTests" //Invovles a lot of BIN copying.
"Nuget" ==> "All"

RunTargetOrDefault "Help"
