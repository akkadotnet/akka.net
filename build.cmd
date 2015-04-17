@echo off

pushd %~dp0

src\.nuget\NuGet.exe update -self

src\.nuget\NuGet.exe install FAKE -OutputDirectory src\packages -ExcludeVersion -Version 3.4.1

src\.nuget\NuGet.exe install xunit.runner.console -OutputDirectory src\packages\FAKE -ExcludeVersion -Version 2.0.0

if not exist src\packages\SourceLink.Fake\tools\SourceLink.fsx ( 
  src\.nuget\nuget.exe install SourceLink.Fake -OutputDirectory src\packages -ExcludeVersion
)
rem cls

set encoding=utf-8
src\packages\FAKE\tools\FAKE.exe build.fsx %*

popd


