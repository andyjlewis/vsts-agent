agentpid={processId}
agentprocessname="{agentProcessName}"
downloadagentfolder="{latestAgent}"
downloadagentbinfolder="{Path.Combine(latestAgent, Constants.Path.BinDirectory)}"
downloadagentexternalsfolder="{Path.Combine(latestAgent, Constants.Path.ExternalsDirectory)}"
existingagentfolder="{currentAgent}"
existingagentbinfolder="{Path.Combine(currentAgent, Constants.Path.BinDirectory)}"
existingagentexternalsfolder="{Path.Combine(currentAgent, Constants.Path.ExternalsDirectory)}"
backupbinfolder="{Path.Combine(currentAgent, $"{Constants.Path.BinDirectory}.bak.{Constants.Agent.Version}")}"
backupexternalsfolder="{Path.Combine(currentAgent, $"{Constants.Path.ExternalsDirectory}.bak.{Constants.Agent.Version}")}"
logfile="{updateLog}"

date "+[%F %T-%4N] --------whoami--------" >> "$logfile" 2>&1
whoami >> "$logfile" 2>&1
date "+[%F %T-%4N] --------whoami--------" >> "$logfile" 2>&1

date "+[%F %T-%4N] Waiting for $agentprocessname ($agentpid) to complete" >> "$logfile" 2>&1"
while [ -e /proc/$agentpid ]"
do"
    date "+[%F %T-%4N] Process $agentpid still running" >> "$logfile" 2>&1"
    sleep 1"
done"
date "+[%F %T-%4N] Process $agentpid finished running" >> "$logfile" 2>&1"

date "+[%F %T-%4N] Sleep 1 more second to make sure process exited" >> "$logfile" 2>&1"
sleep 1"

date "+[%F %T-%4N] Renaming folders and copying files" >> "$logfile""
date "+[%F %T-%4N] move $existingagentbinfolder $backupbinfolder" >> "$logfile" 2>&1"
mv -fv "$existingagentbinfolder" "$backupbinfolder" >> "$logfile" 2>&1"
if [ $? -ne 0 ]"
    then"
        date "+[%F %T-%4N] Can't move $existingagentbinfolder to $backupbinfolder" >> "$logfile" 2>&1"
        exit 1"
fi"

date "+[%F %T-%4N] move $existingagentexternalsfolder $backupexternalsfolder" >> "$logfile" 2>&1"
mv -fv "$existingagentexternalsfolder" "$backupexternalsfolder" >> "$logfile" 2>&1"
if [ $? -ne 0 ]"
    then"
        date "+[%F %T-%4N] Can't move $existingagentexternalsfolder to $backupexternalsfolder" >> "$logfile" 2>&1"
        exit 1"
fi"

date "+[%F %T-%4N] move $downloadagentbinfolder $existingagentbinfolder" >> "$logfile" 2>&1"
mv -fv "$downloadagentbinfolder" "$existingagentbinfolder" >> "$logfile" 2>&1"
if [ $? -ne 0 ]"
    then"
        date "+[%F %T-%4N] Can't move $downloadagentbinfolder to $existingagentbinfolder" >> "$logfile" 2>&1"
        exit 1"
fi"

date "+[%F %T-%4N] move $downloadagentexternalsfolder $existingagentexternalsfolder" >> "$logfile" 2>&1"
mv -fv "$downloadagentexternalsfolder" "$existingagentexternalsfolder" >> "$logfile" 2>&1"
if [ $? -ne 0 ]"
    then"
        date "+[%F %T-%4N] Can't move $downloadagentexternalsfolder to $existingagentexternalsfolder" >> "$logfile" 2>&1"
        exit 1"
fi"

date "+[%F %T-%4N] copy $downloadagentfolder/*.* $existingagentfolder" >> "$logfile" 2>&1"
cp -fv "$downloadagentfolder"/*.* "$existingagentfolder" >> "$logfile" 2>&1"
if [ $? -ne 0 ]"
    then"
        date "+[%F %T-%4N] Can't copy $downloadagentfolder/*.* to $existingagentfolder" >> "$logfile" 2>&1"
        exit 1"
fi"

if (restartInteractiveAgent)
{
    "date "+[%F %T-%4N] Restarting interactive agent"  >> "$logfile" 2>&1"
    ""$existingagentbinfolder"/$agentprocessname &"
}

"date "+[%F %T-%4N] Exit _update.sh" >> "$logfile" 2>&1"