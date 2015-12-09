#!/bin/bash
SCRIPT_PATH="${BASH_SOURCE[0]}";
if ([ -h "${SCRIPT_PATH}" ]) then
  while([ -h "${SCRIPT_PATH}" ]) do SCRIPT_PATH=`readlink "${SCRIPT_PATH}"`; done
fi
pushd . > /dev/null
cd `dirname ${SCRIPT_PATH}` > /dev/null
SCRIPT_PATH=`pwd`;
popd  > /dev/null

if ! [ -f $SCRIPT_PATH/src/.nuget/nuget.exe ] 
    then
        wget "https://www.nuget.org/nuget.exe" -P $SCRIPT_PATH/src/.nuget/
fi

mono $SCRIPT_PATH/src/.nuget/nuget.exe update -self


SCRIPT_PATH="${BASH_SOURCE[0]}";
if ([ -h "${SCRIPT_PATH}" ]) then
  while([ -h "${SCRIPT_PATH}" ]) do SCRIPT_PATH=`readlink "${SCRIPT_PATH}"`; done
fi
pushd . > /dev/null
cd `dirname ${SCRIPT_PATH}` > /dev/null
SCRIPT_PATH=`pwd`;
popd  > /dev/null

mono $SCRIPT_PATH/src/.nuget/NuGet.exe update -self

mono $SCRIPT_PATH/src/.nuget/NuGet.exe install FAKE -OutputDirectory $SCRIPT_PATH/src/packages -ExcludeVersion -Version 3.28.8

mono $SCRIPT_PATH/src/.nuget/NuGet.exe install xunit.runners -OutputDirectory $SCRIPT_PATH/src/packages/FAKE -ExcludeVersion -Version 2.0.0
mono $SCRIPT_PATH/src/.nuget/NuGet.exe install nunit.runners -OutputDirectory $SCRIPT_PATH/src/packages/FAKE -ExcludeVersion -Version 2.6.4

mono $SCRIPT_PATH/src/.nuget/NuGet.exe install NBench.Runner -OutputDirectory $SCRIPT_PATH/src/packages -ExcludeVersion
 

if ! [ -e $SCRIPT_PATH/src/packages/SourceLink.Fake/tools/SourceLink.fsx ] ; then
	mono $SCRIPT_PATH/src/.nuget/NuGet.exe install SourceLink.Fake -OutputDirectory $SCRIPT_PATH/src/packages -ExcludeVersion

fi

export encoding=utf-8

mono $SCRIPT_PATH/src/packages/FAKE/tools/FAKE.exe build.fsx "$@"
