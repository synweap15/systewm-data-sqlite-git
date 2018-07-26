@ECHO OFF

::
:: test_net_standard_20.bat --
::
:: .NET Standard 2.0 Multiplexing Wrapper Tool for Unit Tests
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

REM SET DFLAGS=/L

%_VECHO% DFlags = '%DFLAGS%'

SET FFLAGS=/V /F /G /H /I /R /Y /Z

%_VECHO% FFlags = '%FFLAGS%'

SET ROOT=%~dp0\..
SET ROOT=%ROOT:\\=\%

%_VECHO% Root = '%ROOT%'

SET TOOLS=%~dp0
SET TOOLS=%TOOLS:~0,-1%

%_VECHO% Tools = '%TOOLS%'

CALL :fn_ResetErrorLevel

%__ECHO3% CALL "%TOOLS%\set_common.bat"

IF ERRORLEVEL 1 (
  ECHO Could not set common variables.
  GOTO errors
)

IF NOT DEFINED DOTNET (
  SET DOTNET=dotnet.exe
)

%_VECHO% DotNet = '%DOTNET%'

IF NOT DEFINED TEST_CONFIGURATIONS (
  SET TEST_CONFIGURATIONS=Release
)

%_VECHO% TestConfigurations = '%TEST_CONFIGURATIONS%'

IF NOT DEFINED YEARS (
  SET YEARS=NetStandard20
)

%_VECHO% Years = '%YEARS%'

IF NOT DEFINED NATIVE_YEARS (
  SET NATIVE_YEARS=2017 2015
)

%_VECHO% NativeYears = '%NATIVE_YEARS%'

IF NOT DEFINED TEST_FILE (
  SET TEST_FILE=Tests\all.eagle
)

%_VECHO% TestFile = '%TEST_FILE%'
%_VECHO% PreArgs = '%PREARGS%'
%_VECHO% PostArgs = '%POSTARGS%'

IF NOT DEFINED EAGLESHELL (
  SET EAGLESHELL=EagleShell.dll
)

%_VECHO% EagleShell = '%EAGLESHELL%'

CALL :fn_VerifyDotNetCore

%__ECHO2% PUSHD "%ROOT%"

IF ERRORLEVEL 1 (
  ECHO Could not change directory to "%ROOT%".
  GOTO errors
)

SET TEST_ALL=1

FOR %%C IN (%TEST_CONFIGURATIONS%) DO (
  FOR %%Y IN (%YEARS%) DO (
    FOR %%N IN (%NATIVE_YEARS%) DO (
      IF EXIST "bin\%%Y\%%C\bin" (
        IF EXIST "bin\%%N\%PLATFORM%\%%C" (
          %__ECHO% "%DOTNET%" exec "Externals\Eagle\bin\netStandard20\%EAGLESHELL%" %PREARGS% -anyInitialize "set test_year {%%Y}; set test_native_year {%%N}; set test_configuration {%%C}" -file "%TEST_FILE%" %POSTARGS%

          IF ERRORLEVEL 1 (
            ECHO Testing of "%%Y/%%N/%%C" .NET Standard 2.0 assembly failed.
            GOTO errors
          )
        ) ELSE (
          %_AECHO% Native directory "bin\%%N\%PLATFORM%\%%C" not found, skipped.
        )
      ) ELSE (
        %_AECHO% Managed directory "bin\%%Y\%%C\bin" not found, skipped.
      )
    )
  )
)

%__ECHO2% POPD

IF ERRORLEVEL 1 (
  ECHO Could not restore directory.
  GOTO errors
)

GOTO no_errors

:fn_VerifyDotNetCore
  FOR %%T IN (%DOTNET%) DO (
    SET %%T_PATH=%%~dp$PATH:T
  )
  IF NOT DEFINED %DOTNET%_PATH (
    ECHO The .NET Core executable "%DOTNET%" is required to be in the PATH.
    GOTO errors
  )
  GOTO :EOF

:fn_UnsetVariable
  SETLOCAL
  SET VALUE=%1
  IF DEFINED VALUE (
    SET VALUE=
    ENDLOCAL
    SET %VALUE%=
  ) ELSE (
    ENDLOCAL
  )
  CALL :fn_ResetErrorLevel
  GOTO :EOF

:fn_ResetErrorLevel
  VERIFY > NUL
  GOTO :EOF

:fn_SetErrorLevel
  VERIFY MAYBE 2> NUL
  GOTO :EOF

:usage
  ECHO.
  ECHO Usage: %~nx0
  GOTO errors

:errors
  CALL :fn_SetErrorLevel
  ENDLOCAL
  ECHO.
  ECHO Test failure, errors were encountered.
  GOTO end_of_file

:no_errors
  CALL :fn_ResetErrorLevel
  ENDLOCAL
  ECHO.
  ECHO Test success, no errors were encountered.
  GOTO end_of_file

:end_of_file
%__ECHO% EXIT /B %ERRORLEVEL%
