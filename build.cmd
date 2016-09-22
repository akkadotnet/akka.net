@echo off

pushd %~dp0

SETLOCAL
SET CACHED_NUGET=%LocalAppData%\NuGet\NuGet.exe

IF EXIST %CACHED_NUGET% goto copynuget
echo Downloading latest version of NuGet.exe...
IF NOT EXIST %LocalAppData%\NuGet md %LocalAppData%\NuGet
@powershell -NoProfile -ExecutionPolicy unrestricted -Command "$ProgressPreference = 'SilentlyContinue'; Invoke-WebRequest 'https://www.nuget.org/nuget.exe' -OutFile '%CACHED_NUGET%'"

:copynuget
IF EXIST src\.nuget\nuget.exe goto restore
md src\.nuget
copy %CACHED_NUGET% src\.nuget\nuget.exe > nul

:restore

src\.nuget\NuGet.exe update -self


pushd %~dp0

src\.nuget\NuGet.exe update -self

src\.nuget\NuGet.exe install FAKE -ConfigFile src\.nuget\Nuget.Config -OutputDirectory src\packages -ExcludeVersion -Version 4.16.1

src\.nuget\NuGet.exe install NUnit.Console -ConfigFile src\.nuget\Nuget.Config -OutputDirectory src\packages\FAKE -ExcludeVersion -Version 3.2.1
src\.nuget\NuGet.exe install xunit.runner.console -ConfigFile src\.nuget\Nuget.Config -OutputDirectory src\packages\FAKE -ExcludeVersion -Version 2.0.0
src\.nuget\NuGet.exe install NBench.Runner -OutputDirectory src\packages -ExcludeVersion -Version 0.3.1
src\.nuget\NuGet.exe install Microsoft.SourceBrowser -OutputDirectory src\packages -ExcludeVersion

if not exist src\packages\SourceLink.Fake\tools\SourceLink.fsx (
  src\.nuget\nuget.exe install SourceLink.Fake -ConfigFile src\.nuget\Nuget.Config -OutputDirectory src\packages -ExcludeVersion
)
rem cls

set encoding=utf-8
src\packages\FAKE\tools\FAKE.exe build.fsx %*

popd
