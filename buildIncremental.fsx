#I @"tools/FAKE/tools"
#r "FakeLib.dll"

open System
open System.IO

open Fake
open Fake.Git

module IncrementalTests =   

    let akkaDefaultBranch = "dev"

    type Supports =
    | Windows
    | Linux
    | All

    type Runtime =
    | NetCore
    | Net

    let SkippedTest name runtime =
        match (name, runtime) with
            | (EndsWith "Sqlite.Tests.csproj", NetCore) -> false
            | _ -> true


    let (|IsRunnable|_|) name platform (csproj:string) =
        let isSupported =
            match platform with
            | Windows _ -> isWindows
            | Linux _ -> isLinux
            | All _ -> true
        if (csproj.Contains(name) && isSupported) then Some(name)
        else None

    let IsRunnable testProject =
        match testProject with
        | IsRunnable "Akka.API.Tests.csproj" Linux proj -> false
        | _ -> true

    let getUnitTestProjects runtime =
        let allTestProjects = !! "./**/core/**/*.Tests.csproj"
                              ++ "./**/contrib/**/*.Tests.csproj"
                              -- "./**/serializers/**/*Wire*.csproj"
        allTestProjects 
        |> Seq.filter IsRunnable
        |> Seq.filter (fun p -> SkippedTest p runtime) // filter out specs that should not be run based on .NET Core / .NET differences

    let isBuildScript (file:string) =
        match file with
        | EndsWith "fsx" -> true
        | EndsWith "ps1" -> true
        | EndsWith "cmd" -> true
        | EndsWith "sh" -> true
        | EndsWith "props" -> true // use common.props to trigger full build
        | _ -> false
    
    let getHeadHashFor repositoryDir branch =
        let _, msg, error = runGitCommand repositoryDir (sprintf "log --oneline -1 %s" branch)
        if error <> "" then failwithf "git log --oneline failed: %s" error
        let logMsg = msg |> Seq.head
        let tmp =
            logMsg.Split(' ')
            |> Seq.head
            |> fun s -> s.Split('m')
        if tmp |> Array.length > 2 then tmp.[1].Substring(0,6) else tmp.[0].Substring(0,6)
    
    let getBranchesFileDiff repositoryDir branch =
        let _, msg, error = runGitCommand repositoryDir (sprintf "diff %s --name-status" branch)
        if error <> "" then failwithf "diff %s --name-status failed: %s" branch error
        msg
        |> Seq.map (fun line -> 
            let a = line.Split('\t')
            FileStatus.Parse a.[0],a.[1])
    
    let getUpstreamRemote repositoryDir =
        let _, msg, error = runGitCommand repositoryDir "remote add upstream https://github.com/akkadotnet/akka.net"
        match error with
        | "" -> log "added upstream remote"
        | "fatal: remote upstream already exists." -> log "upstream remote already exists"
        | _ -> failwithf "remote add upstream https://github.com/akkadotnet/akka.net failed: %s" error

    let getUpdatedFiles() = 
        let srcDir = __SOURCE_DIRECTORY__
        log "Adding upstream remote..."
        getUpstreamRemote srcDir
        log "Fetching upstream remote to compare HEAD file changes with..."
        directRunGitCommandAndFail srcDir "fetch upstream"
        getBranchesFileDiff srcDir (sprintf "upstream/%s" akkaDefaultBranch)
        |> Seq.map (fun (_, fi) -> FullName fi)
        |> Seq.filter (fun fi -> (isInFolder (new DirectoryInfo("./src")) (new FileInfo(fi))) || (isBuildScript fi))
  
    // Gather all of the folder paths that contain .csproj files
    let getAllProjectFolders() =
        !! "./src/**/*.csproj"
        |> Seq.map (fun f -> DirectoryName (FullName f))

    // Check if the altered file is inside of any of the folder paths that contain .csproj files
    let isInProjectFolder projectFolder file = 
        isInFolder (new DirectoryInfo(projectFolder)) (new FileInfo(file))
    
    type ProjectFileInclude = { projectFolder: string; file: string; contains: bool  }

    // Return a collection of all projectFolder, altered file, and true/false is contained within
    let generateContainingProjFileCollection alteredFiles =
        getAllProjectFolders()
        |> Seq.map (fun p -> alteredFiles |> Seq.map (fun f -> { projectFolder = p; file = f; contains = isInProjectFolder p f })) 
        |> Seq.concat

    // Find the .csproj file contained within a folder that contains an altered file
    let findCsprojFilesForAlteredFiles fileProjectContainsSeq =
        let findCsprojFileFor fileProjectContains =
            //let projectFolder, file, contains = fileProjectContains
            match fileProjectContains.contains with
            | true -> Some(!! (fileProjectContains.projectFolder @@ "*.csproj") |> Seq.head)
            | false -> None
        fileProjectContainsSeq
        |> Seq.map (fun x -> findCsprojFileFor x) 
        |> Seq.choose id
        |> Seq.map (fun x -> filename x)

    let getAssemblyForProject project =
        try
            !! ("src" @@ "**" @@ "bin" @@ "Release" @@ "net452" @@ fileNameWithoutExt project + ".dll")
            |> Seq.head
        with 
        | :? System.ArgumentException as ex ->
            logf "Could not find built assembly for %s.  Make sure project is built in Release config." (fileNameWithoutExt project);
            reraise()
    
    let getNetCoreAssemblyForProject project =
        try
            !! ("src" @@ "**" @@ "bin" @@ "Release" @@ "netcoreapp1.1" @@ fileNameWithoutExt project + ".dll")
            |> Seq.head
        with 
        | :? System.ArgumentException as ex ->
            logfn "Could not find built assembly for %s.  Make sure project is built in Release config." (fileNameWithoutExt project);
            null

    //-------------------------------------------------------------------------------- 
    // MultiNodeTestRunner incremental test selection
    //--------------------------------------------------------------------------------

    let getMntrProjects runtime =
        !! "./src/**/*Tests.MultiNode.csproj"
        |> Seq.map (fun x -> x.ToString())
        |> Seq.filter (fun p -> SkippedTest p runtime) // filter out specs that should not be run based on .NET Core / .NET differences
    
    let getAllMntrTestAssemblies() = // if we're not running incremental tests
        getMntrProjects Net
        |> Seq.map (fun x -> getAssemblyForProject x)
        |> Seq.filter (fun p -> SkippedTest p Net) // filter out specs that should not be run based on .NET Core / .NET differences
    
    let getAllMntrTestNetCoreAssemblies() = // if we're not running incremental tests
        getMntrProjects NetCore
        |> Seq.map (fun x -> getNetCoreAssemblyForProject x)
    
    //--------------------------------------------------------------------------------
    // Performance tests incremental test selection
    //--------------------------------------------------------------------------------

    let getPerfTestProjects() =
        !! "./src/**/*Tests.Performance.csproj"
        |> Seq.map (fun x -> x.ToString())
    
    let getAllPerfTestAssemblies() = //if we're not running incremental tests
        getPerfTestProjects()
        |> Seq.map (fun x -> getAssemblyForProject x)
  
    //--------------------------------------------------------------------------------
    // Recursive dependency search
    //--------------------------------------------------------------------------------

    type ProjectPath = { projectName: string; projectPath: string }
    type ProjectDetails = { parentProject: ProjectPath; dependencies: ProjectPath seq; isTestProject: bool }
       
    let FullNameLinux baseProj csprojRef =
        let csprojRef = normalizePath csprojRef
        let mutable workingDirPath = DirectoryName baseProj
        let csprojRefParts = split '/' csprojRef
        let rootPath =
            csprojRefParts 
            |> List.filter (fun x -> x = "..") 
            |> List.iter (fun x -> workingDirPath <- (DirectoryName workingDirPath))
            workingDirPath
        let subPath = 
            csprojRefParts 
            |> List.filter (fun x -> not (x = "..")) 
            |> List.reduce (@@)
        rootPath @@ subPath

    let getDependentProjectFilePath baseProj csprojRef =
        match isWindows with
        | true -> FullName csprojRef
        | _ -> FullNameLinux baseProj csprojRef
        
    let getDependentProjects csprojFile =
        XMLRead true csprojFile "" "" "//Project/ItemGroup/ProjectReference/@Include"
        |> Seq.map (fun p -> { projectName = filename (normalizePath p); projectPath = getDependentProjectFilePath csprojFile p })
    
    type TestMode =
    | Unit
    | MNTR
    | Perf

    let isTestProject csproj testMode =
        match testMode with
        | Unit -> (filename csproj).Contains("Tests.csproj")
        | MNTR -> (filename csproj).Contains("Tests.MultiNode.csproj")
        | Perf -> (filename csproj).Contains("Tests.Performance.csproj")

    let getAllProjectDependencies testMode =
        !! "./src/**/*.csproj"
        |> Seq.map (fun f -> { parentProject = { projectName = filename f; projectPath = f }; dependencies = getDependentProjects f; isTestProject = isTestProject f testMode })
    
    let rec findTestProjectsThatHaveDependencyOn project testMode =
        let allProjects = getAllProjectDependencies testMode
        seq { for proj in allProjects do
                for dep in proj.dependencies do
                    if (proj.parentProject.projectName = project && proj.isTestProject) then
                        // if the altered project is a test project (e.g. Akka.Tests)
                        yield proj;
                    if (dep.projectName = project && proj.isTestProject) then
                        //logfn "%s references %s and is a test project..." proj.parentProject.projectName project
                        yield proj
                    elif (dep.projectName = project && not proj.isTestProject) then
                        //logfn "%s references %s but is not a test project..." proj.parentProject.projectName project
                        yield! findTestProjectsThatHaveDependencyOn proj.parentProject.projectName testMode }
    
    let getIncrementalTestProjects2 testMode runtime =
        logfn "Searching for incremental tests to run in %s test mode..." (testMode.ToString())
        let updatedFiles = getUpdatedFiles()
        log "The following files have been updated since forking from dev branch..."
        updatedFiles |> Seq.iter (fun x -> logfn "\t%s" x)
        log "The following test projects will be run..."
        if (updatedFiles |> Seq.exists (fun p -> isBuildScript p)) then
            log "Full test suite"
            let specs =
                match testMode with
                | Unit -> getUnitTestProjects runtime
                | MNTR -> getMntrProjects runtime
                | Perf -> getPerfTestProjects()
            specs
            |> Seq.filter (fun p -> SkippedTest p runtime) // filter out specs that should not be run based on .NET Core / .NET differences
        else
            updatedFiles
            |> generateContainingProjFileCollection
            |> findCsprojFilesForAlteredFiles
            |> Seq.map (fun p -> findTestProjectsThatHaveDependencyOn p testMode)
            |> Seq.concat
            |> Seq.map (fun p -> p.parentProject.projectPath)
            |> Seq.distinct
            |> Seq.filter IsRunnable
            |> Seq.filter (fun p -> SkippedTest p runtime) // filter out specs that should not be run based on .NET Core / .NET differences
    
    let getIncrementalUnitTests runtime =
        getIncrementalTestProjects2 Unit runtime
    
    let getIncrementalMNTRTests() =
        getIncrementalTestProjects2 MNTR Net
        |> Seq.map (fun p -> getAssemblyForProject p)
    
    let getIncrementalNetCoreMNTRTests() =
        getIncrementalTestProjects2 MNTR NetCore
        |> Seq.map (fun p -> getNetCoreAssemblyForProject p)
    
    let getIncrementalPerfTests() =
        getIncrementalTestProjects2 Perf Net
        |> Seq.map (fun p -> getAssemblyForProject p)
