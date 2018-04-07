#!/bin/bash

scriptdir=`dirname "$BASH_SOURCE"`
pushd "$scriptdir/.."

dotnet build SQLite.NET.NetStandard20.MSBuild.sln /property:Configuration=Debug /property:ConfigurationSuffix=$SQLITE_NET_CONFIGURATION_SUFFIX /property:InteropCodec=false /property:InteropLog=false /property:CheckState=true /property:CountHandle=true /property:TraceConnection=true /property:TraceDetection=true /property:TraceHandle=true /property:TraceStatement=true /property:TrackMemoryBytes=true "$@"

popd
