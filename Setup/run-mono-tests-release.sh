#!/bin/bash

scriptdir=`dirname "$BASH_SOURCE"`

if [[ "$OSTYPE" == "darwin"* ]]; then
  libname=libSQLite.Interop.dylib
else
  libname=libSQLite.Interop.so
fi

pushd "$scriptdir/.."
mono Externals/Eagle/bin/EagleShell.exe -preInitialize "set test_configuration Release; set build_directory {bin/2013/Release/bin}; set interop_assembly_file_names $libname" -file Tests/all.eagle
popd
