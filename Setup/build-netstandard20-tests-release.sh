#!/bin/bash

scriptdir=`dirname "$BASH_SOURCE"`
pushd "$scriptdir/.."

dotnet build SQLite.NET.NetStandard20.MSBuild.sln /property:Configuration=Release /property:ConfigurationSuffix=NetStandard20 /property:InteropCodec=false /property:InteropLog=false "$@"

popd
