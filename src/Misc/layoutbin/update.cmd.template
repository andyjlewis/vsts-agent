@echo off

:: agent will replace key words in the template and generate a batch script to run.
:: Keywords: 
::  PROCESSID = pid
::  AGENTPROCESSNAME = agent.listener[.exe]
::  ROOTFOLDER = ./
::  EXISTAGENTVERSION = 2.100.0
::  DOWNLOADAGENTVERSION = 2.101.0 
::  UPDATELOG = _diag/SelfUpdate-UTC.log
::  RESTARTINTERACTIVEAGENT = 0/1

setlocal
set agentpid=_PROCESS_ID_
set agentprocessname=_AGENT_PROCESS_NAME_
set rootfolder=_ROOT_FOLDER_
set existagentversion=_EXIST_AGENT_VERSION_
set downloadagentversion=_DOWNLOAD_AGENT_VERSION_
set logfile=_UPDATE_LOG_
set restartinteractiveagent=_RESTART_INTERACTIVE_AGENT_

:: log user who run the script
echo [%date% %time%] --------whoami-------- >> "%logfile%" 2>&1
whoami >> "%logfile%" 2>&1
echo [%date% %time%] --------whoami-------- >> "%logfile%" 2>&1

:: wait for agent process to exit.
echo [%date% %time%] Waiting for %agentprocessname% (%agentpid%) to complete >> "%logfile%" 2>&1
:loop
tasklist /fi "pid eq %agentpid%" | find /I "%agentprocessname%" 2>nul
if ERRORLEVEL 1  (
  goto copy
)

echo [%date% %time%] Process %agentpid% still running, check again after 1 second. >> "%logfile%" 2>&1
timeout /t 1 /nobreak >nul
goto loop

:: start re-organize folders
:copy
echo [%date% %time%] Process %agentpid% finished running >> "%logfile%" 2>&1
echo [%date% %time%] Sleep 1 more second to make sure process exited >> "%logfile%" 2>&1
timeout /t 1 /nobreak >nul
echo [%date% %time%] Re-organize folders >> "%logfile%" 2>&1

:: the folder structure under agent root will be
:: ./bin -> bin.2.100.0 (junction folder)
:: ./externals -> externals.2.100.0 (junction folder)
:: ./bin.2.100.0
:: ./externals.2.100.0
:: ./bin.2.99.0
:: ./externals.2.99.0
:: by using the juction folder we can avoid file in use problem.

:: if the bin/externals junction point already exist, we just need to delete the juction point then re-create to point to new bin/externals folder.
:: if the bin/externals still are real folders, we need to rename the existing folder to bin.version format then create junction point to new bin/externals folder.

:: check bin folder
dir "%rootfolder%" /AL | findstr "bin" >> "%logfile%" 2>&1
if ERRORLEVEL 1 (
  rem return code 1 means it can't find a bin folder that is a junction folder
  rem so we need to move the current bin folder to bin.2.99.0 folder.
  echo [%date% %time%] move "%rootfolder%\bin" "%rootfolder%\bin.%existagentversion%" >> "%logfile%" 2>&1
  move "%rootfolder%\bin" "%rootfolder%\bin.%existagentversion%" >> "%logfile%" 2>&1
  if ERRORLEVEL 1 (
    echo [%date% %time%] Can't move "%rootfolder%\bin" to "%rootfolder%\bin.%existagentversion%" >> "%logfile%" 2>&1
    goto fail
  )
  
) else (
  rem otherwise it find a bin folder that is a junction folder
  rem we just need to delete the junction point.
  echo [%date% %time%] Delete existing junction bin folder >> "%logfile%" 2>&1
  rmdir "%rootfolder%\bin" >> "%logfile%" 2>&1
  if ERRORLEVEL 1 (
    echo [%date% %time%] Can't delete existing junction bin folder >> "%logfile%" 2>&1
    goto fail
  )
)

:: check externals folder
dir "%rootfolder%" /AL | findstr "externals" >> "%logfile%" 2>&1
if ERRORLEVEL 1 (
  rem return code 1 means it can't find a externals folder that is a junction folder
  rem so we need to move the current externals folder to externals.2.99.0 folder.
  echo [%date% %time%] move "%rootfolder%\externals" "%rootfolder%\externals.%existagentversion%" >> "%logfile%" 2>&1
  move "%rootfolder%\externals" "%rootfolder%\externals.%existagentversion%" >> "%logfile%" 2>&1
  if ERRORLEVEL 1 (
    echo [%date% %time%] Can't move "%rootfolder%\externals" to "%rootfolder%\externals.%existagentversion%" >> "%logfile%" 2>&1
    goto fail
  )  
) else (
  rem otherwise it find a externals folder that is a junction folder
  rem we just need to delete the junction point.
  echo [%date% %time%] Delete existing junction externals folder >> "%logfile%" 2>&1
  rmdir "%rootfolder%\externals" >> "%logfile%" 2>&1
  if ERRORLEVEL 1 (
    echo [%date% %time%] Can't delete existing junction externals folder >> "%logfile%" 2>&1
    goto fail
  )
)

:: create junction bin folder
echo [%date% %time%] Create junction bin folder >> "%logfile%" 2>&1
mklink /J "%rootfolder%\bin" "%rootfolder%\bin.%downloadagentversion%" >> "%logfile%" 2>&1
if ERRORLEVEL 1 (
  echo [%date% %time%] Can't create junction bin folder >> "%logfile%" 2>&1
  goto fail
)

:: create junction externals folder
echo [%date% %time%] Create junction externals folder >> "%logfile%" 2>&1
mklink /J "%rootfolder%\externals" "%rootfolder%\externals.%downloadagentversion%" >> "%logfile%" 2>&1
if ERRORLEVEL 1 (
  echo [%date% %time%] Can't create junction externals folder >> "%logfile%" 2>&1
  goto fail
)

echo [%date% %time%] Update succeed >> "%logfile%" 2>&1

:: rename the update log file with %logfile%.succeed/.failed/succeedneedrestart
:: agent service host can base on the log file name determin the result of the agent update
echo [%date% %time%] Rename "%logfile%" to be "%logfile%.succeed" >> "%logfile%" 2>&1
move "%logfile%" "%logfile%.succeed"

:: restart interactive agent if needed
if %restartinteractiveagent% equ 1 (
  echo [%date% %time%] Restart interactive agent >> "%logfile%.succeed" 2>&1
  endlocal
  start "Vsts Agent" cmd.exe /k "_ROOT_FOLDER_\bin\_AGENT_PROCESS_NAME_"
) else (
  endlocal
)

goto end

:fail
echo [%date% %time%] Rename "%logfile%" to be "%logfile%.failed" >> "%logfile%" 2>&1
move "%logfile%" "%logfile%.failed"
goto end

:end
