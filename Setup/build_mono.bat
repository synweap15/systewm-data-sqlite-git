@ECHO OFF

::
:: build_mono.bat --
::
:: Mono Wrapper Tool for MSBuild
::
:: Written by Joe Mistachkin.
:: Released to the public domain, use at your own risk!
::

SETLOCAL

REM SET __ECHO=ECHO
REM SET __ECHO3=ECHO
IF NOT DEFINED _AECHO (SET _AECHO=REM)
IF NOT DEFINED _CECHO (SET _CECHO=REM)
IF NOT DEFINED _VECHO (SET _VECHO=REM)

%_AECHO% Running %0 %*

SET DUMMY2=%1

IF DEFINED DUMMY2 (
  GOTO usage
)

SET TOOLS=%~dp0
SET TOOLS=%TOOLS:~0,-1%

%_VECHO% Tools = '%TOOLS%'

IF DEFINED BUILD_DEBUG (
  SET BUILD_CONFIGURATIONS=DebugManagedOnly ReleaseManagedOnly
) ELSE (
  SET BUILD_CONFIGURATIONS=ReleaseManagedOnly
)

SET PLATFORMS="Any CPU"
SET YEARS=2013
SET NOUSER=1
SET MSBUILD_ARGS=/property:ConfigurationSuffix=MonoOnPosix
SET MSBUILD_ARGS=%MSBUILD_ARGS% /property:InteropCodec=false
SET MSBUILD_ARGS=%MSBUILD_ARGS% /property:InteropLog=false

IF DEFINED MSBUILD_ARGS_MONO (
  SET MSBUILD_ARGS=%MSBUILD_ARGS% %MSBUILD_ARGS_MONO%
)

SET MSBUILD_ARGS_DEBUG=/property:CheckState=true
SET MSBUILD_ARGS_DEBUG=%MSBUILD_ARGS_DEBUG% /property:CountHandle=true
SET MSBUILD_ARGS_DEBUG=%MSBUILD_ARGS_DEBUG% /property:TraceConnection=true
SET MSBUILD_ARGS_DEBUG=%MSBUILD_ARGS_DEBUG% /property:TraceDetection=true
SET MSBUILD_ARGS_DEBUG=%MSBUILD_ARGS_DEBUG% /property:TraceHandle=true
SET MSBUILD_ARGS_DEBUG=%MSBUILD_ARGS_DEBUG% /property:TraceStatement=true
SET MSBUILD_ARGS_DEBUG=%MSBUILD_ARGS_DEBUG% /property:TrackMemoryBytes=true

CALL :fn_ResetErrorLevel

%__ECHO3% CALL "%TOOLS%\build_all.bat"

IF ERRORLEVEL 1 (
  ECHO Failed to build Mono binaries.
  GOTO errors
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
