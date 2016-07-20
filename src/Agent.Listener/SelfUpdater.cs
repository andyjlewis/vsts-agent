﻿using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.VisualStudio.Services.Agent.Listener
{
    [ServiceLocator(Default = typeof(SelfUpdater))]
    public interface ISelfUpdater : IAgentService
    {
        Task<bool> SelfUpdate(IJobDispatcher jobDispatcher, bool restartInteractiveAgent, CancellationToken token);
    }

    public class SelfUpdater : AgentService, ISelfUpdater
    {
        private static string _packageType = "agent";
        private static string _platform = BuildConstants.AgentPackage.PackageName;

        private PackageMetadata _latestPackage;

        public async Task<bool> SelfUpdate(IJobDispatcher jobDispatcher, bool restartInteractiveAgent, CancellationToken token)
        {
            if (!await UpdateNeeded(token))
            {
                Trace.Info($"Can't find availiable update package.");
                return false;
            }

            Trace.Info($"An update is availiable.");

            // Print console line that warn user not shutdown agent.
            var terminal = HostContext.GetService<ITerminal>();
            terminal.WriteLine(StringUtil.Loc("UpdateInProcess"));

            string latestAgent = await DownloadLatestAgent(token);
            Trace.Info($"Download latest agent into: {latestAgent}");

            // wait till all running job finish
            await jobDispatcher.WaitAsync(token);
            Trace.Info($"All running job has exited.");

            // delete previous backup agent
            // bin.bak.2.99.0
            // externals.bak.2.99.0
            foreach (string existBackUp in Directory.GetDirectories(IOUtil.GetRootPath(), "*.bak.*"))
            {
                Trace.Info($"Delete existing agent backup at {existBackUp}.");
                IOUtil.DeleteDirectory(existBackUp, token);
            }

            // delete old bin.2.99.0
            var allBinDirs = Directory.GetDirectories(IOUtil.GetRootPath(), "bin.*");
            if (allBinDirs.Length > 1)
            {
                // there are more than 1 bin.version folder.
                // delete older bin.version folders.
                foreach (var oldBinDir in allBinDirs)
                {
                    if (string.Equals(oldBinDir, Path.Combine(IOUtil.GetRootPath(), $"bin.{Constants.Agent.Version}"), StringComparison.OrdinalIgnoreCase))
                    {
                        // skip for current agent version
                        continue;
                    }

                    IOUtil.DeleteDirectory(oldBinDir, token);
                }
            }

            // delete old externals.2.99.0
            var allExternalsDirs = Directory.GetDirectories(IOUtil.GetRootPath(), "externals.*");
            if (allExternalsDirs.Length > 1)
            {
                // there are more than 1 externals.version folder.
                // delete older externals.version folders.
                foreach (var oldExternalDir in allExternalsDirs)
                {
                    if (string.Equals(oldExternalDir, Path.Combine(IOUtil.GetRootPath(), $"externals.{Constants.Agent.Version}"), StringComparison.OrdinalIgnoreCase))
                    {
                        // skip for current agent version
                        continue;
                    }

                    IOUtil.DeleteDirectory(oldExternalDir, token);
                }
            }

            // move latest agent in place
            // move from _work/_update -> bin.version and externals.version under root, copy and replace all .sh/.cmd files
            Directory.Move(Path.Combine(latestAgent, WellKnownDirectory.Bin.ToString()), Path.Combine(IOUtil.GetRootPath(), $"{WellKnownDirectory.Bin}.{_latestPackage.Version}"));
            Directory.Move(Path.Combine(latestAgent, WellKnownDirectory.Externals.ToString()), Path.Combine(IOUtil.GetRootPath(), $"{WellKnownDirectory.Externals}.{_latestPackage.Version}"));
            IOUtil.CopyDirectory(latestAgent, IOUtil.GetRootPath(), token);

            // locate upgrade script and run it.
            // generate update script
#if OS_WINDOWS
            string updateScript = GenerateBatchScript(latestAgent, restartInteractiveAgent);
#elif (OS_OSX || OS_LINUX)
            string updateScript = GenerateShellScript(latestAgent, restartInteractiveAgent);
#endif
            Trace.Info($"Generate update script into: {updateScript}");

            // kick off update script
            Process invokeScript = new Process();
            var whichUtil = HostContext.GetService<IWhichUtil>();
#if OS_WINDOWS
            invokeScript.StartInfo.FileName = whichUtil.Which("cmd.exe");
            invokeScript.StartInfo.Arguments = $"/c \"{updateScript}\"";
#elif (OS_OSX || OS_LINUX)
            invokeScript.StartInfo.FileName = whichUtil.Which("bash");
            invokeScript.StartInfo.Arguments = $"-c \"{updateScript}\"";
#endif
            invokeScript.Start();
            Trace.Info($"Update script start running");

            return true;
        }

        private async Task<bool> UpdateNeeded(CancellationToken token)
        {
            var agentServer = HostContext.GetService<IAgentServer>();
            var packages = await agentServer.GetPackagesAsync(_packageType, _platform, 1, token);
            if (packages == null || packages.Count == 0)
            {
                Trace.Info($"There is no package for {_packageType} and {_platform}.");
                return false;
            }

            _latestPackage = packages.FirstOrDefault();
            Trace.Info($"Latest version of '{_latestPackage.Type}' package availiable in server is {_latestPackage.Version}");
            PackageVersion serverVersion = new PackageVersion(_latestPackage.Version);

            Trace.Info($"Current running agent version is {Constants.Agent.Version}");
            PackageVersion agentVersion = new PackageVersion(Constants.Agent.Version);

            return serverVersion.CompareTo(agentVersion) > 0;
        }

        /// <summary>
        /// _work
        ///     \_update
        ///            \bin
        ///            \externals
        ///            \run.sh
        ///            \run.cmd
        ///            \package.zip //temp download .zip/.tar.gz
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        private async Task<string> DownloadLatestAgent(CancellationToken token)
        {
            var agentServer = HostContext.GetService<IAgentServer>();
            string latestAgentDirectory = IOUtil.GetUpdatePath(HostContext);
            IOUtil.DeleteDirectory(latestAgentDirectory, token);
            Directory.CreateDirectory(latestAgentDirectory);
            string archiveFile = Path.Combine(latestAgentDirectory, $"{new Uri(_latestPackage.DownloadUrl).Segments.Last()}");

            try
            {
                using (var httpClient = new HttpClient())
                {
                    //open zip stream in async mode
                    using (FileStream fs = new FileStream(archiveFile, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
                    {
                        using (Stream result = await httpClient.GetStreamAsync(_latestPackage.DownloadUrl))
                        {
                            //81920 is the default used by System.IO.Stream.CopyTo and is under the large object heap threshold (85k). 
                            await result.CopyToAsync(fs, 81920, token);
                            await fs.FlushAsync(token);
                        }
                    }
                }

                if (archiveFile.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    ZipFile.ExtractToDirectory(archiveFile, latestAgentDirectory);
                }
                else if (archiveFile.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
                {
                    var whichUtil = HostContext.GetService<IWhichUtil>();
                    string tar = whichUtil.Which("tar");
                    if (string.IsNullOrEmpty(tar))
                    {
                        throw new NotSupportedException($"tar -xzf");
                    }

                    // tar -xzf
                    using (var processInvoker = HostContext.CreateService<IProcessInvoker>())
                    {
                        processInvoker.OutputDataReceived += new EventHandler<ProcessDataReceivedEventArgs>((sender, args) =>
                        {
                            if (!string.IsNullOrEmpty(args.Data))
                            {
                                Trace.Info(args.Data);
                            }
                        });

                        processInvoker.ErrorDataReceived += new EventHandler<ProcessDataReceivedEventArgs>((sender, args) =>
                        {
                            if (!string.IsNullOrEmpty(args.Data))
                            {
                                Trace.Error(args.Data);
                            }
                        });

                        int exitCode = await processInvoker.ExecuteAsync(latestAgentDirectory, tar, $"-xzf {archiveFile}", null, token);
                        if (exitCode != 0)
                        {
                            throw new NotSupportedException($"Can't use 'tar -xzf' extract archive file: {archiveFile}. return code: {exitCode}.");
                        }
                    }
                }
                else
                {
                    throw new NotSupportedException($"{archiveFile}");
                }

                Trace.Info($"Finished getting latest agent package at: {latestAgentDirectory}.");
            }
            finally
            {
                try
                {
                    // delete .zip file
                    if (!string.IsNullOrEmpty(archiveFile) && File.Exists(archiveFile))
                    {
                        Trace.Verbose("Deleting latest agent package zip: {0}", archiveFile);
                        IOUtil.DeleteFile(archiveFile);
                    }
                }
                catch (Exception ex)
                {
                    //it is not critical if we fail to delete the temp folder
                    Trace.Warning("Failed to delete agent package zip '{0}'. Exception: {1}", archiveFile, ex);
                }
            }

            return latestAgentDirectory;
        }

        private string GenerateBatchScript(string latestAgent, bool restartInteractiveAgent)
        {
            int processId = Process.GetCurrentProcess().Id;
            string updateLog = Path.Combine(IOUtil.GetDiagPath(), $"SelfUpdate-{DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")}.log");
            string currentAgent = IOUtil.GetRootPath();
            string agentProcessName = "Agent.Listener.exe";

            StringBuilder scriptBuilder = new StringBuilder();
            scriptBuilder.AppendLine("@echo off");
            scriptBuilder.AppendLine("setlocal");
            scriptBuilder.AppendLine($"set agentpid={processId}");
            scriptBuilder.AppendLine($"set agentprocessname=\"{agentProcessName}\"");
            scriptBuilder.AppendLine($"set downloadagentfolder=\"{latestAgent}\"");
            scriptBuilder.AppendLine($"set downloadagentbinfolder=\"{Path.Combine(latestAgent, Constants.Path.BinDirectory)}\"");
            scriptBuilder.AppendLine($"set downloadagentexternalsfolder=\"{Path.Combine(latestAgent, Constants.Path.ExternalsDirectory)}\"");
            scriptBuilder.AppendLine($"set existingagentfolder=\"{currentAgent}\"");
            scriptBuilder.AppendLine($"set existingagentbinfolder=\"{Path.Combine(currentAgent, Constants.Path.BinDirectory)}\"");
            scriptBuilder.AppendLine($"set existingagentexternalsfolder=\"{Path.Combine(currentAgent, Constants.Path.ExternalsDirectory)}\"");
            scriptBuilder.AppendLine($"set backupbinfolder=\"{Path.Combine(currentAgent, $"{Constants.Path.BinDirectory}.bak.{Constants.Agent.Version}")}\"");
            scriptBuilder.AppendLine($"set backupexternalsfolder=\"{Path.Combine(currentAgent, $"{Constants.Path.ExternalsDirectory}.bak.{Constants.Agent.Version}")}\"");
            scriptBuilder.AppendLine($"set logfile=\"{updateLog}\"");

            scriptBuilder.AppendLine("echo [%date% %time%] --------env-------- >> %logfile% 2>&1");
            scriptBuilder.AppendLine("set >> %logfile% 2>&1");
            scriptBuilder.AppendLine("echo [%date% %time%] --------env-------- >> %logfile% 2>&1");

            scriptBuilder.AppendLine("echo [%date% %time%] --------whoami-------- >> %logfile% 2>&1");
            scriptBuilder.AppendLine("whoami >> %logfile% 2>&1");
            scriptBuilder.AppendLine("echo [%date% %time%] --------whoami-------- >> %logfile% 2>&1");

            scriptBuilder.AppendLine("echo [%date% %time%] Waiting for %agentprocessname% (%agentpid%) to complete >> %logfile% 2>&1");
            scriptBuilder.AppendLine(":loop");
            scriptBuilder.AppendLine("tasklist /fi \"pid eq %agentpid%\" | find /I \"%agentprocessname%\" 2>nul");
            scriptBuilder.AppendLine("if \"%errorlevel%\"==\"1\" goto copy");
            scriptBuilder.AppendLine("echo [%date% %time%] Process %agentpid% still running >> %logfile% 2>&1");
            scriptBuilder.AppendLine("timeout /t 1 /nobreak >nul");
            scriptBuilder.AppendLine("goto loop");

            scriptBuilder.AppendLine(":copy");
            scriptBuilder.AppendLine("echo [%date% %time%] Process %agentpid% finished running >> %logfile% 2>&1");
            scriptBuilder.AppendLine("echo [%date% %time%] Sleep 1 more second to make sure process exited >> %logfile% 2>&1");
            scriptBuilder.AppendLine("timeout /t 1 /nobreak >nul");
            scriptBuilder.AppendLine("echo [%date% %time%] Renameing folders and copying files >> %logfile% 2>&1");

            scriptBuilder.AppendLine("echo [%date% %time%] move %existingagentbinfolder% %backupbinfolder% >> %logfile% 2>&1");
            scriptBuilder.AppendLine("move %existingagentbinfolder% %backupbinfolder% >> %logfile% 2>&1");
            scriptBuilder.AppendLine("if \"%errorlevel%\" gtr \"0\" (echo [%date% %time%] Can't move %existingagentbinfolder% to %backupbinfolder% >> %logfile% 2>&1 & goto end)");

            scriptBuilder.AppendLine("echo [%date% %time%] move %existingagentexternalsfolder% %backupexternalsfolder% >> %logfile% 2>&1");
            scriptBuilder.AppendLine("move %existingagentexternalsfolder% %backupexternalsfolder% >> %logfile% 2>&1");
            scriptBuilder.AppendLine("if \"%errorlevel%\" gtr \"0\" (echo [%date% %time%] Can't move %existingagentexternalsfolder% to %backupexternalsfolder% >> %logfile% 2>&1 & goto end)");

            scriptBuilder.AppendLine("echo [%date% %time%] move %downloadagentbinfolder% %existingagentbinfolder% >> %logfile% 2>&1");
            scriptBuilder.AppendLine("move %downloadagentbinfolder% %existingagentbinfolder% >> %logfile% 2>&1");
            scriptBuilder.AppendLine("if \"%errorlevel%\" gtr \"0\" (echo [%date% %time%] Can't move %downloadagentbinfolder% to %existingagentbinfolder% >> %logfile% 2>&1 & goto end)");

            scriptBuilder.AppendLine("echo [%date% %time%] move %downloadagentexternalsfolder% %existingagentexternalsfolder% >> %logfile% 2>&1");
            scriptBuilder.AppendLine("move %downloadagentexternalsfolder% %existingagentexternalsfolder% >> %logfile% 2>&1");
            scriptBuilder.AppendLine("if \"%errorlevel%\" gtr \"0\" (echo [%date% %time%] Can't move %downloadagentexternalsfolder% to %existingagentexternalsfolder% >> %logfile% 2>&1 & goto end)");

            scriptBuilder.AppendLine("echo [%date% %time%] copy %downloadagentfolder%\\*.* %existingagentfolder%\\*.* /Y >> %logfile% 2>&1");
            scriptBuilder.AppendLine("copy %downloadagentfolder%\\*.* %existingagentfolder%\\*.* /Y >> %logfile% 2>&1");
            scriptBuilder.AppendLine("if \"%errorlevel%\" gtr \"0\" (echo [%date% %time%] Can't copy %downloadagentfolder%\\*.* to %existingagentfolder%\\*.* >> %logfile% 2>&1 & goto end)");

            if (restartInteractiveAgent)
            {
                scriptBuilder.AppendLine("echo [%date% %time%] Restart interactive agent >> %logfile% 2>&1");
                scriptBuilder.AppendLine("endlocal");
                scriptBuilder.AppendLine($"start \"Vsts Agent\" cmd.exe /k \"{Path.Combine(currentAgent, Constants.Path.BinDirectory, agentProcessName)}\"");
            }
            else
            {
                scriptBuilder.AppendLine("endlocal");
            }

            scriptBuilder.AppendLine("echo [%date% %time%] Exit _update.cmd >> %logfile% 2>&1");
            scriptBuilder.AppendLine(":end");

            string updateScript = Path.Combine(IOUtil.GetWorkPath(HostContext), "_update.cmd");
            if (File.Exists(updateScript))
            {
                IOUtil.DeleteFile(updateScript);
            }

            File.WriteAllText(updateScript, scriptBuilder.ToString());

            return updateScript;
        }

        private string GenerateShellScript(string latestAgent, bool restartInteractiveAgent)
        {
            int processId = Process.GetCurrentProcess().Id;
            string updateLog = Path.Combine(IOUtil.GetDiagPath(), $"SelfUpdate-{DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")}.log");
            string currentAgent = IOUtil.GetRootPath();
            string agentProcessName = "Agent.Listener";

            StringBuilder scriptBuilder = new StringBuilder();
            scriptBuilder.AppendLine($"agentpid={processId}");
            scriptBuilder.AppendLine($"agentprocessname=\"{agentProcessName}\"");
            scriptBuilder.AppendLine($"downloadagentfolder=\"{latestAgent}\"");
            scriptBuilder.AppendLine($"downloadagentbinfolder=\"{Path.Combine(latestAgent, Constants.Path.BinDirectory)}\"");
            scriptBuilder.AppendLine($"downloadagentexternalsfolder=\"{Path.Combine(latestAgent, Constants.Path.ExternalsDirectory)}\"");
            scriptBuilder.AppendLine($"existingagentfolder=\"{currentAgent}\"");
            scriptBuilder.AppendLine($"existingagentbinfolder=\"{Path.Combine(currentAgent, Constants.Path.BinDirectory)}\"");
            scriptBuilder.AppendLine($"existingagentexternalsfolder=\"{Path.Combine(currentAgent, Constants.Path.ExternalsDirectory)}\"");
            scriptBuilder.AppendLine($"backupbinfolder=\"{Path.Combine(currentAgent, $"{Constants.Path.BinDirectory}.bak.{Constants.Agent.Version}")}\"");
            scriptBuilder.AppendLine($"backupexternalsfolder=\"{Path.Combine(currentAgent, $"{Constants.Path.ExternalsDirectory}.bak.{Constants.Agent.Version}")}\"");
            scriptBuilder.AppendLine($"logfile=\"{updateLog}\"");

            scriptBuilder.AppendLine("date \"+[%F %T-%4N] --------env--------\" >> \"$logfile\" 2>&1");
            scriptBuilder.AppendLine("env >> \"$logfile\" 2>&1");
            scriptBuilder.AppendLine("date \"+[%F %T-%4N] --------env--------\" >> \"$logfile\" 2>&1");

            scriptBuilder.AppendLine("date \"+[%F %T-%4N] --------whoami--------\" >> \"$logfile\" 2>&1");
            scriptBuilder.AppendLine("whoami >> \"$logfile\" 2>&1");
            scriptBuilder.AppendLine("date \"+[%F %T-%4N] --------whoami--------\" >> \"$logfile\" 2>&1");

            scriptBuilder.AppendLine("date \"+[%F %T-%4N] Waiting for $agentprocessname ($agentpid) to complete\" >> \"$logfile\" 2>&1");
            scriptBuilder.AppendLine("while [ -e /proc/$agentpid ]");
            scriptBuilder.AppendLine("do");
            scriptBuilder.AppendLine("    date \"+[%F %T-%4N] Process $agentpid still running\" >> \"$logfile\" 2>&1");
            scriptBuilder.AppendLine("    sleep 1");
            scriptBuilder.AppendLine("done");
            scriptBuilder.AppendLine("date \"+[%F %T-%4N] Process $agentpid finished running\" >> \"$logfile\" 2>&1");

            scriptBuilder.AppendLine("date \"+[%F %T-%4N] Sleep 1 more second to make sure process exited\" >> \"$logfile\" 2>&1");
            scriptBuilder.AppendLine("sleep 1");

            scriptBuilder.AppendLine("date \"+[%F %T-%4N] Renaming folders and copying files\" >> \"$logfile\"");
            scriptBuilder.AppendLine("date \"+[%F %T-%4N] move $existingagentbinfolder $backupbinfolder\" >> \"$logfile\" 2>&1");
            scriptBuilder.AppendLine("mv -fv \"$existingagentbinfolder\" \"$backupbinfolder\" >> \"$logfile\" 2>&1");
            scriptBuilder.AppendLine("if [ $? -ne 0 ]");
            scriptBuilder.AppendLine("    then");
            scriptBuilder.AppendLine("        date \"+[%F %T-%4N] Can't move $existingagentbinfolder to $backupbinfolder\" >> \"$logfile\" 2>&1");
            scriptBuilder.AppendLine("        exit 1");
            scriptBuilder.AppendLine("fi");

            scriptBuilder.AppendLine("date \"+[%F %T-%4N] move $existingagentexternalsfolder $backupexternalsfolder\" >> \"$logfile\" 2>&1");
            scriptBuilder.AppendLine("mv -fv \"$existingagentexternalsfolder\" \"$backupexternalsfolder\" >> \"$logfile\" 2>&1");
            scriptBuilder.AppendLine("if [ $? -ne 0 ]");
            scriptBuilder.AppendLine("    then");
            scriptBuilder.AppendLine("        date \"+[%F %T-%4N] Can't move $existingagentexternalsfolder to $backupexternalsfolder\" >> \"$logfile\" 2>&1");
            scriptBuilder.AppendLine("        exit 1");
            scriptBuilder.AppendLine("fi");

            scriptBuilder.AppendLine("date \"+[%F %T-%4N] move $downloadagentbinfolder $existingagentbinfolder\" >> \"$logfile\" 2>&1");
            scriptBuilder.AppendLine("mv -fv \"$downloadagentbinfolder\" \"$existingagentbinfolder\" >> \"$logfile\" 2>&1");
            scriptBuilder.AppendLine("if [ $? -ne 0 ]");
            scriptBuilder.AppendLine("    then");
            scriptBuilder.AppendLine("        date \"+[%F %T-%4N] Can't move $downloadagentbinfolder to $existingagentbinfolder\" >> \"$logfile\" 2>&1");
            scriptBuilder.AppendLine("        exit 1");
            scriptBuilder.AppendLine("fi");

            scriptBuilder.AppendLine("date \"+[%F %T-%4N] move $downloadagentexternalsfolder $existingagentexternalsfolder\" >> \"$logfile\" 2>&1");
            scriptBuilder.AppendLine("mv -fv \"$downloadagentexternalsfolder\" \"$existingagentexternalsfolder\" >> \"$logfile\" 2>&1");
            scriptBuilder.AppendLine("if [ $? -ne 0 ]");
            scriptBuilder.AppendLine("    then");
            scriptBuilder.AppendLine("        date \"+[%F %T-%4N] Can't move $downloadagentexternalsfolder to $existingagentexternalsfolder\" >> \"$logfile\" 2>&1");
            scriptBuilder.AppendLine("        exit 1");
            scriptBuilder.AppendLine("fi");

            scriptBuilder.AppendLine("date \"+[%F %T-%4N] copy $downloadagentfolder/*.* $existingagentfolder\" >> \"$logfile\" 2>&1");
            scriptBuilder.AppendLine("cp -fv \"$downloadagentfolder\"/*.* \"$existingagentfolder\" >> \"$logfile\" 2>&1");
            scriptBuilder.AppendLine("if [ $? -ne 0 ]");
            scriptBuilder.AppendLine("    then");
            scriptBuilder.AppendLine("        date \"+[%F %T-%4N] Can't copy $downloadagentfolder/*.* to $existingagentfolder\" >> \"$logfile\" 2>&1");
            scriptBuilder.AppendLine("        exit 1");
            scriptBuilder.AppendLine("fi");

            if (restartInteractiveAgent)
            {
                scriptBuilder.AppendLine("date \"+[%F %T-%4N] Restarting interactive agent\"  >> \"$logfile\" 2>&1");
                scriptBuilder.AppendLine("\"$existingagentbinfolder\"/$agentprocessname &");
            }

            scriptBuilder.AppendLine("date \"+[%F %T-%4N] Exit _update.sh\" >> \"$logfile\" 2>&1");

            string updateScript = Path.Combine(IOUtil.GetWorkPath(HostContext), "_update.sh");
            if (File.Exists(updateScript))
            {
                IOUtil.DeleteFile(updateScript);
            }

            File.WriteAllText(updateScript, scriptBuilder.ToString());

            return updateScript;
        }
    }
}
