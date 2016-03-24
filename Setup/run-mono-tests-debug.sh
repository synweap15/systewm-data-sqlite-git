#!/bin/bash

scriptdir=`dirname "$BASH_SOURCE"`

if [[ "$OSTYPE" == "darwin"* ]]; then
  libname=libsqlite3.dylib
else
  libname=libsqlite3.so.0
fi

pushd "$scriptdir/.."
mono Externals/Eagle/bin/EagleShell.exe -preInitialize "set test_configuration Debug; set build_directory {bin/2013/Debug/bin}; set native_library_file_names $libname" -file Tests/all.eagle
popd
