#!/bin/bash

scriptdir=`dirname "$BASH_SOURCE"`

pushd "$scriptdir/.."
mono Externals/Eagle/bin/EagleShell.exe -preInitialize "set test_configuration Debug; set build_directory {bin/2013/Debug/bin}; set native_library_file_names libsqlite3.so.0" -file Tests/all.eagle
popd
