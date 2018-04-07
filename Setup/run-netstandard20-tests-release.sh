#!/bin/bash

scriptdir=`dirname "$BASH_SOURCE"`

if [[ "$OSTYPE" == "darwin"* ]]; then
  libname=libSQLite.Interop.dylib
else
  libname=libSQLite.Interop.so
fi

if [[ -z "$SQLITE_NET_YEAR" ]]; then
  SQLITE_NET_YEAR=2013
fi

pushd "$scriptdir/.."

SQLITE_INTEROP_DIR=bin/$SQLITE_NET_YEAR/Release$SQLITE_NET_CONFIGURATION_SUFFIX/bin
SQLITE_INTEROP_FILE=$SQLITE_INTEROP_DIR/$libname

if [[ -f "${SQLITE_INTEROP_FILE}" ]]; then
  cp "$SQLITE_INTEROP_FILE" "$SQLITE_INTEROP_DIR/SQLite.Interop.dll"
fi

libname=SQLite.Interop.dll

dotnet exec Externals/Eagle/bin/netStandard20/EagleShell.dll -preInitialize "set test_configuration Release; set test_year NetStandard20; set test_native_year $SQLITE_NET_YEAR; set interop_assembly_file_names $libname" -initialize -postInitialize "unset no(deleteSqliteImplicitNativeFiles); unset no(copySqliteImplicitNativeFiles)" -file Tests/all.eagle "$@"

popd
