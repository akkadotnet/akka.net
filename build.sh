#!/usr/bin/env bash
##########################################################################
# This is the Fake bootstrapper script for Linux and OS X.
##########################################################################

# Define directories.
SCRIPT_DIR=$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )
TOOLS_DIR=$SCRIPT_DIR/tools
NUGET_EXE=$TOOLS_DIR/nuget.exe
NUGET_URL=https://dist.nuget.org/win-x86-commandline/v4.0.0/nuget.exe
FAKE_VERSION=4.61.2
FAKE_EXE=$TOOLS_DIR/FAKE/tools/FAKE.exe
DOTNET_VERSION=1.0.4
DOTNET_INSTALLER_URL=https://raw.githubusercontent.com/dotnet/cli/rel/1.0.0/scripts/obtain/dotnet-install.sh

# Define default arguments.
TARGET="Default"
CONFIGURATION="Release"
VERBOSITY="verbose"
DRYRUN=
SCRIPT_ARGUMENTS=()

# Parse arguments.
for i in "$@"; do
    case $1 in
        -t|--target) TARGET="$2"; shift ;;
        -c|--configuration) CONFIGURATION="$2"; shift ;;
        -v|--verbosity) VERBOSITY="$2"; shift ;;
        -d|--dryrun) DRYRUN="-dryrun" ;;
        --) shift; SCRIPT_ARGUMENTS+=("$@"); break ;;
        *) SCRIPT_ARGUMENTS+=("$1") ;;
    esac
    shift
done

# Make sure the tools folder exist.
if [ ! -d "$TOOLS_DIR" ]; then
  mkdir "$TOOLS_DIR"
fi

###########################################################################
# INSTALL .NET CORE CLI
###########################################################################

echo "Installing .NET CLI..."
if [ ! -d "$SCRIPT_DIR/.dotnet" ]; then
  mkdir "$SCRIPT_DIR/.dotnet"
fi
curl -Lsfo "$SCRIPT_DIR/.dotnet/dotnet-install.sh" $DOTNET_INSTALLER_URL
bash "$SCRIPT_DIR/.dotnet/dotnet-install.sh" --version $DOTNET_VERSION --install-dir .dotnet --no-path
export PATH="$SCRIPT_DIR/.dotnet":$PATH
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
chmod -R 0755 ".dotnet"
"$SCRIPT_DIR/.dotnet/dotnet" --info

###########################################################################
# INSTALL NUGET
###########################################################################

# Download NuGet if it does not exist.
if [ ! -f "$NUGET_EXE" ]; then
    echo "Downloading NuGet..."
    curl -Lsfo "$NUGET_EXE" $NUGET_URL
    if [ $? -ne 0 ]; then
        echo "An error occured while downloading nuget.exe."
        exit 1
    fi
fi

###########################################################################
# INSTALL FAKE
###########################################################################

if [ ! -f "$FAKE_EXE" ]; then
    mono "$NUGET_EXE" install Fake -ExcludeVersion -Version $FAKE_VERSION -OutputDirectory "$TOOLS_DIR"
    if [ $? -ne 0 ]; then
        echo "An error occured while installing Cake."
        exit 1
    fi
fi

# Make sure that Fake has been installed.
if [ ! -f "$FAKE_EXE" ]; then
    echo "Could not find Fake.exe at '$FAKE_EXE'."
    exit 1
fi

###########################################################################
# WORKAROUND FOR MONO
###########################################################################
export FrameworkPathOverride=/usr/lib/mono/4.5/

###########################################################################
# RUN BUILD SCRIPT
###########################################################################

# Start Fake
exec mono "$FAKE_EXE" build.fsx "${SCRIPT_ARGUMENTS[@]}" --verbosity=$VERBOSITY --configuration=$CONFIGURATION --target=$TARGET $DRYRUN
