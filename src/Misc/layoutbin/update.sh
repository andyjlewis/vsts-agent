#!/bin/bash

# agent will replace key words in the template and generate a batch script to run.
# Keywords: 
#  PROCESSID = pid
#  AGENTPROCESSNAME = agent.listener[.exe]
#  ROOTFOLDER = ./
#  EXISTAGENTVERSION = 2.100.0
#  DOWNLOADAGENTVERSION = 2.101.0 
#  UPDATELOG = _diag/SelfUpdate-UTC.log
#  RESTARTINTERACTIVEAGENT = 0/1

agentpid=_PROCESS_ID_
agentprocessname=_AGENT_PROCESS_NAME_
rootfolder="_ROOT_FOLDER_"
existagentversion=_EXIST_AGENT_VERSION_
downloadagentversion=_DOWNLOAD_AGENT_VERSION_
logfile="_UPDATE_LOG_"
restartinteractiveagent=_RESTART_INTERACTIVE_AGENT_

# log user who run the script
date "+[%F %T-%4N] --------whoami--------" >> "$logfile" 2>&1
whoami >> "$logfile" 2>&1
date "+[%F %T-%4N] --------whoami--------" >> "$logfile" 2>&1

# wait for agent process to exit.
date "+[%F %T-%4N] Waiting for $agentprocessname ($agentpid) to complete" >> "$logfile" 2>&1
while [ -e /proc/$agentpid ]
do
    date "+[%F %T-%4N] Process $agentpid still running" >> "$logfile" 2>&1
    sleep 1
done
date "+[%F %T-%4N] Process $agentpid finished running" >> "$logfile" 2>&1

# start re-organize folders
date "+[%F %T-%4N] Sleep 1 more second to make sure process exited" >> "$logfile" 2>&1
sleep 1

# the folder structure under agent root will be
# ./bin -> bin.2.100.0 (junction folder)
# ./externals -> externals.2.100.0 (junction folder)
# ./bin.2.100.0
# ./externals.2.100.0
# ./bin.2.99.0
# ./externals.2.99.0
# by using the juction folder we can avoid file in use problem.

# if the bin/externals junction point already exist, we just need to delete the juction point then re-create to point to new bin/externals folder.
# if the bin/externals still are real folders, we need to rename the existing folder to bin.version format then create junction point to new bin/externals folder.

# check bin folder
if [[ -L "$rootfolder/bin" && -d "$rootfolder/bin" ]]
then
    # return code 0 means it find a bin folder that is a junction folder
    # we just need to delete the junction point.
    date "+[%F %T-%4N] Delete existing junction bin folder" >> "$logfile"
    rm "$rootfolder/bin" >> "$logfile"

#   if %errorlevel% gtr 0 (
#     echo [%date% %time%] Can't delete existing junction bin folder >> %logfile% 2>&1
#     goto fail
#   )
#     echo "$file is a symlink to a directory"
fi

date "+[%F %T-%4N] Renaming folders and copying files" >> "$logfile"
date "+[%F %T-%4N] move $existingagentbinfolder $backupbinfolder" >> "$logfile" 2>&1
mv -fv "$existingagentbinfolder" "$backupbinfolder" >> "$logfile" 2>&1
if [ $? -ne 0 ]
    then
        date "+[%F %T-%4N] Can't move $existingagentbinfolder to $backupbinfolder" >> "$logfile" 2>&1
        exit 1
fi

date "+[%F %T-%4N] move $existingagentexternalsfolder $backupexternalsfolder" >> "$logfile" 2>&1
mv -fv "$existingagentexternalsfolder" "$backupexternalsfolder" >> "$logfile" 2>&1
if [ $? -ne 0 ]
    then
        date "+[%F %T-%4N] Can't move $existingagentexternalsfolder to $backupexternalsfolder" >> "$logfile" 2>&1
        exit 1
fi

date "+[%F %T-%4N] move $downloadagentbinfolder $existingagentbinfolder" >> "$logfile" 2>&1
mv -fv "$downloadagentbinfolder" "$existingagentbinfolder" >> "$logfile" 2>&1
if [ $? -ne 0 ]
    then
        date "+[%F %T-%4N] Can't move $downloadagentbinfolder to $existingagentbinfolder" >> "$logfile" 2>&1
        exit 1
fi

date "+[%F %T-%4N] move $downloadagentexternalsfolder $existingagentexternalsfolder" >> "$logfile" 2>&1
mv -fv "$downloadagentexternalsfolder" "$existingagentexternalsfolder" >> "$logfile" 2>&1
if [ $? -ne 0 ]
    then
        date "+[%F %T-%4N] Can't move $downloadagentexternalsfolder to $existingagentexternalsfolder" >> "$logfile" 2>&1
        exit 1
fi

date "+[%F %T-%4N] copy $downloadagentfolder/*.* $existingagentfolder" >> "$logfile" 2>&1
cp -fv "$downloadagentfolder"/*.* "$existingagentfolder" >> "$logfile" 2>&1
if [ $? -ne 0 ]
    then
        date "+[%F %T-%4N] Can't copy $downloadagentfolder/*.* to $existingagentfolder" >> "$logfile" 2>&1
        exit 1
fi

if [ $restartinteractiveagent -ne 0 ]
    then
        date "+[%F %T-%4N] Restarting interactive agent"  >> "$logfile" 2>&1
        $existingagentbinfolder/$agentprocessname &
fi

date "+[%F %T-%4N] Exit _update.sh" >> "$logfile" 2>&1