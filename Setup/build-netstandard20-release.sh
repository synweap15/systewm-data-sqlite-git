#!/bin/bash

scriptdir=`dirname "$BASH_SOURCE"`
pushd "$scriptdir/.."

dotnet build SQLite.NET.NetStandard20.MSBuild.sln /property:Configuration=Release /property:ConfigurationSuffix=$SQLITE_NET_CONFIGURATION_SUFFIX /property:InteropCodec=false /property:InteropLog=false "$@"

popd
