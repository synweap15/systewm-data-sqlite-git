@ECHO OFF

::
:: build_nuget.bat --
::
:: Wrapper Tool for NuGet
::
:: Written by Joe Mistachkin.
:: Released to the public domain, use at your own risk!
::

SETLOCAL

REM SET __ECHO=ECHO
REM SET __ECHO2=ECHO
REM SET __ECHO3=ECHO
IF NOT DEFINED _AECHO (SET _AECHO=REM)
IF NOT DEFINED _CECHO (SET _CECHO=REM)
IF NOT DEFINED _VECHO (SET _VECHO=REM)

%_AECHO% Running %0 %*

SET DUMMY2=%1

IF DEFINED DUMMY2 (
  GOTO usage
)

SET ROOT=%~dp0\..
SET ROOT=%ROOT:\\=\%

%_VECHO% Root = '%ROOT%'

SET TOOLS=%~dp0
SET TOOLS=%TOOLS:~0,-1%

%_VECHO% Tools = '%TOOLS%'

IF NOT DEFINED NUGET (
  REM
  REM WARNING: This batch tool relies upon a custom fork of the NuGet client that
  REM          implements the "-VerbatimVersion" option.  For further information,
  REM          please see "https://github.com/NuGet/Home/issues/3050".
  REM
  SET NUGET=%ROOT%\Externals\NuGet\NuGet.exe
)

%_VECHO% NuGet = '%NUGET%'

REM
REM NOTE: If the Eagle binaries do not appear to be available, skip doing
REM       things in this batch tool that require them.
REM
IF NOT EXIST "%ROOT%\Externals\Eagle\bin\netFramework40\EagleShell.exe" (
  SET NO_NUGET_VERSION=1
  SET NO_NUGET_XPLATFORM=1
)

CALL :fn_ResetErrorLevel

IF DEFINED NUGET_VERSION GOTO skip_nuGetVersion

IF DEFINED NO_NUGET_VERSION (
  SET NUGET_VERSION=1.0.0.0
  GOTO skip_nuGetVersion
)

%__ECHO2% PUSHD "%ROOT%\Externals\Eagle\bin\netFramework40"

IF ERRORLEVEL 1 (
  ECHO Could not change directory to "%ROOT%\Externals\Eagle\bin\netFramework40".
  GOTO errors
)

SET GET_NUGET_VERSION_CMD=EagleShell.exe -initialize -postInitialize setToolVariables -evaluate "puts stdout [regexp -inline -skip 1 -- $nuget_version_pattern [readFile {%ROOT%\NuGet\SQLite.Core.nuspec}]]"

IF DEFINED __ECHO (
  %__ECHO% %GET_NUGET_VERSION_CMD%
  SET NUGET_VERSION=1.0.X.0
) ELSE (
  FOR /F %%T IN ('%GET_NUGET_VERSION_CMD%') DO (SET NUGET_VERSION=%%T)
)

IF NOT DEFINED NUGET_VERSION (
  ECHO The NUGET_VERSION environment variable could not be set.
  GOTO errors
)

%__ECHO2% POPD

IF ERRORLEVEL 1 (
  ECHO Could not restore directory.
  GOTO errors
)

:skip_nuGetVersion

%_VECHO% NuGetVersion = '%NUGET_VERSION%'

IF NOT EXIST "%ROOT%\Setup\Output" (
  %__ECHO% MKDIR "%ROOT%\Setup\Output"

  IF ERRORLEVEL 1 (
    ECHO Could not create directory "%ROOT%\Setup\Output".
    GOTO errors
  )
)

IF NOT DEFINED LINUX_URI (
  SET LINUX_URI=https://system.data.sqlite.org/index.html/uv/%NUGET_VERSION%/linux-x64/SQLite.Interop.dll
)

IF NOT DEFINED LINUX_DIRECTORY (
  SET LINUX_DIRECTORY=%ROOT%\bin\2016\linux-x64\ReleaseNativeOnly
)

%_VECHO% LinuxUri = '%LINUX_URI%'
%_VECHO% LinuxDirectory = '%LINUX_DIRECTORY%'

IF NOT DEFINED MACOS_URI (
  SET MACOS_URI=https://system.data.sqlite.org/index.html/uv/%NUGET_VERSION%/osx-x64/SQLite.Interop.dll
)

IF NOT DEFINED MACOS_DIRECTORY (
  SET MACOS_DIRECTORY=%ROOT%\bin\2016\osx-x64\ReleaseNativeOnly
)

%_VECHO% MacOsUri = '%MACOS_URI%'
%_VECHO% MacOsDirectory = '%MACOS_DIRECTORY%'

IF NOT DEFINED NO_NUGET_XPLATFORM (
  %_CECHO% "%ROOT%\Externals\Eagle\bin\netFramework40\EagleShell.exe" -evaluate "set directory {%LINUX_DIRECTORY%}; set fileName [file join $directory SQLite.Interop.dll]; file mkdir $directory; catch {file delete $fileName}; uri download -- {%LINUX_URI%} $fileName"
  %__ECHO% "%ROOT%\Externals\Eagle\bin\netFramework40\EagleShell.exe" -evaluate "set directory {%LINUX_DIRECTORY%}; set fileName [file join $directory SQLite.Interop.dll]; file mkdir $directory; catch {file delete $fileName}; uri download -- {%LINUX_URI%} $fileName"

  IF ERRORLEVEL 1 (
    ECHO Download of System.Data.SQLite interop assembly "%LINUX_URI%" to "%LINUX_DIRECTORY%" failure.
    GOTO errors
  ) ELSE (
    %_AECHO% Download of System.Data.SQLite interop assembly "%LINUX_URI%" to "%LINUX_DIRECTORY%" success.
  )

  %_CECHO% "%ROOT%\Externals\Eagle\bin\netFramework40\EagleShell.exe" -evaluate "set directory {%MACOS_DIRECTORY%}; set fileName [file join $directory SQLite.Interop.dll]; file mkdir $directory; catch {file delete $fileName}; uri download -- {%MACOS_URI%} $fileName"
  %__ECHO% "%ROOT%\Externals\Eagle\bin\netFramework40\EagleShell.exe" -evaluate "set directory {%MACOS_DIRECTORY%}; set fileName [file join $directory SQLite.Interop.dll]; file mkdir $directory; catch {file delete $fileName}; uri download -- {%MACOS_URI%} $fileName"

  IF ERRORLEVEL 1 (
    ECHO Download of System.Data.SQLite interop assembly "%MACOS_URI%" to "%MACOS_DIRECTORY%" failure.
    GOTO errors
  ) ELSE (
    %_AECHO% Download of System.Data.SQLite interop assembly "%MACOS_URI%" to "%MACOS_DIRECTORY%" success.
  )
)

%__ECHO% "%NUGET%" pack -VerbatimVersion "%ROOT%\NuGet\SQLite.nuspec"

IF ERRORLEVEL 1 (
  ECHO The "%ROOT%\NuGet\SQLite.nuspec" package could not be built.
  GOTO usage
)

%__ECHO% "%NUGET%" pack -VerbatimVersion "%ROOT%\NuGet\SQLite.Core.nuspec"

IF ERRORLEVEL 1 (
  ECHO The "%ROOT%\NuGet\SQLite.Core.nuspec" package could not be built.
  GOTO usage
)

%__ECHO% "%NUGET%" pack -VerbatimVersion "%ROOT%\NuGet\SQLite.Core.NetFramework.nuspec"

IF ERRORLEVEL 1 (
  ECHO The "%ROOT%\NuGet\SQLite.Core.NetFramework.nuspec" package could not be built.
  GOTO usage
)

%__ECHO% "%NUGET%" pack -VerbatimVersion "%ROOT%\NuGet\SQLite.Core.NetStandard.nuspec"

IF ERRORLEVEL 1 (
  ECHO The "%ROOT%\NuGet\SQLite.Core.NetStandard.nuspec" package could not be built.
  GOTO usage
)

IF NOT DEFINED NUGET_CORE_ONLY (
  %__ECHO% "%NUGET%" pack -VerbatimVersion "%ROOT%\NuGet\SQLite.Core.MSIL.nuspec"

  IF ERRORLEVEL 1 (
    ECHO The "%ROOT%\NuGet\SQLite.Core.MSIL.nuspec" package could not be built.
    GOTO usage
  )
)

%__ECHO% "%NUGET%" pack -VerbatimVersion "%ROOT%\NuGet\SQLite.EF6.nuspec"

IF ERRORLEVEL 1 (
  ECHO The "%ROOT%\NuGet\SQLite.EF6.nuspec" package could not be built.
  GOTO usage
)

%__ECHO% "%NUGET%" pack -VerbatimVersion "%ROOT%\NuGet\SQLite.Linq.nuspec"

IF ERRORLEVEL 1 (
  ECHO The "%ROOT%\NuGet\SQLite.Linq.nuspec" package could not be built.
  GOTO usage
)

IF NOT DEFINED NUGET_CORE_ONLY (
  %__ECHO% "%NUGET%" pack -VerbatimVersion "%ROOT%\NuGet\SQLite.MSIL.nuspec"

  IF ERRORLEVEL 1 (
    ECHO The "%ROOT%\NuGet\SQLite.MSIL.nuspec" package could not be built.
    GOTO usage
  )

  %__ECHO% "%NUGET%" pack -VerbatimVersion "%ROOT%\NuGet\SQLite.x86.nuspec"

  IF ERRORLEVEL 1 (
    ECHO The "%ROOT%\NuGet\SQLite.x86.nuspec" package could not be built.
    GOTO usage
  )

  %__ECHO% "%NUGET%" pack -VerbatimVersion "%ROOT%\NuGet\SQLite.x64.nuspec"

  IF ERRORLEVEL 1 (
    ECHO The "%ROOT%\NuGet\SQLite.x64.nuspec" package could not be built.
    GOTO usage
  )
)

%__ECHO% MOVE *.nupkg "%ROOT%\Setup\Output"

IF ERRORLEVEL 1 (
  ECHO Could not move "*.nupkg" to "%ROOT%\Setup\Output".
  GOTO usage
)

GOTO no_errors

:fn_ResetErrorLevel
  VERIFY > NUL
  GOTO :EOF

:fn_SetErrorLevel
  VERIFY MAYBE 2> NUL
  GOTO :EOF

:usage
  ECHO.
  ECHO Usage: %~nx0
  ECHO.
  GOTO errors

:errors
  CALL :fn_SetErrorLevel
  ENDLOCAL
  ECHO.
  ECHO Build failure, errors were encountered.
  GOTO end_of_file

:no_errors
  CALL :fn_ResetErrorLevel
  ENDLOCAL
  ECHO.
  ECHO Build success, no errors were encountered.
  GOTO end_of_file

:end_of_file
%__ECHO% EXIT /B %ERRORLEVEL%
