#!/bin/bash

scriptdir=`dirname "$BASH_SOURCE"`

if [[ -z "$SQLITE_NET_YEAR" ]]; then
  SQLITE_NET_YEAR=NetStandard20
fi

pushd "$scriptdir/.."
dotnet build SQLite.NET.$SQLITE_NET_YEAR.MSBuild.sln /property:Configuration=Debug /property:ConfigurationSuffix=$SQLITE_NET_CONFIGURATION_SUFFIX /property:InteropCodec=false /property:InteropLog=false /property:CheckState=true /property:CountHandle=true /property:TraceConnection=true /property:TraceDetection=true /property:TraceHandle=true /property:TraceStatement=true /property:TrackMemoryBytes=true "$@"
popd
