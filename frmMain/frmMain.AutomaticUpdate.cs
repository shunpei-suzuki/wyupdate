﻿using System;
using System.IO;
using wyDay.Controls;
using wyUpdate.Common;

namespace wyUpdate
{
    public partial class frmMain
    {
        // Automatic Update Mode (aka API mode)
        UpdateHelper updateHelper;
        bool isAutoUpdateMode;
        string autoUpdateStateFile;

        UpdateStep autoUpdateStepProcessing;


        bool currentlyExtracting;

        void SetupAutoupdateMode()
        {
            isAutoUpdateMode = true;

            updateHelper = new UpdateHelper(this);
            updateHelper.SenderProcessClosed += UpdateHelper_SenderProcessClosed;
            updateHelper.RequestReceived += UpdateHelper_RequestReceived;
        }

        void SetupSelfAutoupdateMode(string pipeName)
        {
            isAutoUpdateMode = true;

            updateHelper = new UpdateHelper(this, pipeName);
            updateHelper.SenderProcessClosed += UpdateHelper_SenderProcessClosed;
            updateHelper.RequestReceived += UpdateHelper_RequestReceived;
        }

        void UpdateHelper_RequestReceived(object sender, Action a, UpdateStep s)
        {
            if (a == Action.Cancel)
            {
                CancelUpdate(true);
                return;
            }

            // filter out out-of-order requests (never assume the step 's' is coming in the correct order)
            if (FilterBadRequest(s))
                return;

            autoUpdateStepProcessing = s;

            switch (s)
            {
                case UpdateStep.CheckForUpdate:

                    CheckForUpdate();

                    break;
                case UpdateStep.DownloadUpdate:

                    DownloadUpdate();

                    break;
                case UpdateStep.BeginExtraction:

                    update.CurrentlyUpdating = UpdateOn.Extracting;
                    InstallUpdates(update.CurrentlyUpdating);

                    break;
                case UpdateStep.RestartInfo:

                    // send a success signal.
                    updateHelper.SendSuccess(autoUpdateStepProcessing);

                    break;
                case UpdateStep.Install:

                    // show self & make topmost
                    Visible = true;
                    TopMost = true;
                    TopMost = false;

                    if (needElevation || willSelfUpdate)
                    {
                        StartSelfElevated();
                        return;
                    }

                    update.CurrentlyUpdating = UpdateOn.ClosingProcesses;
                    InstallUpdates(update.CurrentlyUpdating);

                    break;
            }
        }

        void UpdateHelper_SenderProcessClosed(object sender, EventArgs e)
        {
            // close wyUpdate if we're not installing an update
            if (isAutoUpdateMode && !updateHelper.Installing)
                CancelUpdate(true);
        }

        /// <summary>
        /// Filters bad request by responding with the required info.
        /// </summary>
        /// <param name="s">The requested step.</param>
        /// <returns>True if a bad request has been filtered, false otherwise</returns>
        bool FilterBadRequest(UpdateStep s)
        {
            // for example if they try to check for updates when already checking for updates the request should be rejected.

            // Or: if they request "CheckForUpdate" when showing the update info page we should respond with RequestSucceeded(), and provide the update info


            switch (s)
            {
                case UpdateStep.CheckForUpdate:

                    // if already checking ...
                    if (frameOn == Frame.Checking && downloader != null)
                    {
                        // report progress of 0%
                        updateHelper.SendProgress(0, UpdateStep.CheckForUpdate);
                        return true;
                    }

                    // if on another step ...
                    if (frameOn != Frame.Checking)
                    {
                        // report UpdateAvailable, with changes
                        updateHelper.SendSuccess(update.NewVersion, panelDisplaying.GetChangesRTF(), true, null);

                        return true;
                    }

                    break;
                case UpdateStep.DownloadUpdate:

                    if(frameOn == Frame.Checking)
                    {
                        // waiting to be told to check for updates...
                        if(downloader == null)
                        {
                            // report 0% and begin checking
                            updateHelper.SendProgress(0, UpdateStep.CheckForUpdate);
                            CheckForUpdate();
                        }
                        else // already checking ...
                        {
                            // report 0% progress
                            updateHelper.SendProgress(0, UpdateStep.CheckForUpdate);
                        }

                        return true;
                    }
                    
                    if(frameOn == Frame.InstallUpdates)
                    {
                        // if already downloading ...
                        if(update.CurrentlyUpdating == UpdateOn.DownloadingUpdate)
                        {
                            // report 0%
                            updateHelper.SendProgress(0, UpdateStep.DownloadUpdate);
                        }
                        else // on another step (extracting, etc.) ...
                        {
                            // report UpdateDownloaded
                            updateHelper.SendSuccess(UpdateStep.DownloadUpdate);
                        }

                        return true;
                    }

                    break;
                case UpdateStep.BeginExtraction:

                    if (frameOn == Frame.Checking)
                    {
                        // waiting to be told to check for updates...
                        if (downloader == null)
                        {
                            // report 0% and begin checking
                            updateHelper.SendProgress(0, UpdateStep.CheckForUpdate);
                            CheckForUpdate();
                        }
                        else // already checking ...
                        {
                            // report 0% progress
                            updateHelper.SendProgress(0, UpdateStep.CheckForUpdate);
                        }

                        return true;
                    }

                    // if we haven't downloaded yet...
                    if (frameOn == Frame.UpdateInfo)
                    {
                        // report 0% progress & download
                        updateHelper.SendProgress(0, UpdateStep.DownloadUpdate);
                        DownloadUpdate();
                    }

                    if (frameOn == Frame.InstallUpdates)
                    {
                        // if already downloading ...
                        if (update.CurrentlyUpdating == UpdateOn.DownloadingUpdate)
                        {
                            // report 0%
                            updateHelper.SendProgress(0, UpdateStep.DownloadUpdate);
                            return true;
                        }

                        // if done extracting...
                        if(updtDetails != null)
                        {
                            // report extraction completed successfully
                            updateHelper.SendSuccess(UpdateStep.BeginExtraction);
                            return true;
                        }

                        if (currentlyExtracting)
                        {
                            // report extraction has begun
                            updateHelper.SendProgress(0, UpdateStep.BeginExtraction);
                            return true;
                        }
                    }


                    break;
                case UpdateStep.RestartInfo:
                case UpdateStep.Install:


                    //TODO: if there isn't an update ready to install - 

                    break;
            }


            // no bad request found - continue processing as usual
            return false;
        }


        string CreateAutoUpdateTempFolder()
        {
            string temp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                       "wyUpdate AU");

            // if the folder temp folder doesn't exist, create the folder with hiden attributes
            if(!Directory.Exists(temp))
            {
                Directory.CreateDirectory(temp);

                File.SetAttributes(temp, FileAttributes.System | FileAttributes.Hidden);
            }

            temp = Path.Combine(temp, "cache\\" + update.GUID);

            Directory.CreateDirectory(temp);

            return temp;
        }


        void PrepareStepOn(UpdateStepOn step)
        {
            switch (step)
            {
                case UpdateStepOn.Checking:

                    ShowFrame(Frame.Checking);

                    break;

                case UpdateStepOn.UpdateAvailable:

                    ShowFrame(Frame.UpdateInfo);

                    break;

                case UpdateStepOn.UpdateDownloaded:

                    // set the update step pending (extracting)
                    update.CurrentlyUpdating = UpdateOn.Extracting;

                    needElevation = NeedElevationToUpdate();

                    // show frame InstallUpdate
                    ShowFrame(Frame.InstallUpdates);

                    // put a checkmark next to downloaded
                    panelDisplaying.UpdateItems[0].Status = UpdateItemStatus.Success;

                    break;

                case UpdateStepOn.UpdateReadyToInstall:

                    string updtDetailsFilename = Path.Combine(tempDirectory, "updtdetails.udt");

                    // Try to load the update details file

                    if (File.Exists(updtDetailsFilename))
                    {
                        updtDetails = UpdateDetails.Load(updtDetailsFilename);
                    }
                    else
                        throw new Exception("Update details file does not exist.");

                    // set the update step pending (closing processes & installing files, etc.)
                    update.CurrentlyUpdating = UpdateOn.ClosingProcesses;

                    needElevation = NeedElevationToUpdate();

                    // show frame InstallUpdate
                    ShowFrame(Frame.InstallUpdates);

                    // put a checkmark next to downloaded
                    panelDisplaying.UpdateItems[0].Status = UpdateItemStatus.Success;

                    // set the "Extracting" text
                    SetStepStatus(1, clientLang.Extract);

                    break;

                default:
                    throw new Exception("Can't restore from this automatic update state: " + step);
            }
        }


        void SaveAutoUpdateData(UpdateStepOn updateStepOn)
        {
            FileStream fs = new FileStream(autoUpdateStateFile, FileMode.Create, FileAccess.Write);

            // Write any file-identification data you want to here
            WriteFiles.WriteHeader(fs, "IUAUFV1");

            // Step on {Checked = 2, Downloaded = 4, Extracted = 6}
            WriteFiles.WriteInt(fs, 0x01, (int)updateStepOn);

            // DateTime when the last step was taken.
            WriteFiles.WriteLong(fs, 0x02, DateTime.Now.ToBinary());

            // file to execute
            if (updateHelper.FileToExecuteAfterUpdate != null)
                WriteFiles.WriteString(fs, 0x03, updateHelper.FileToExecuteAfterUpdate);

            if (updateHelper.AutoUpdateID != null)
                WriteFiles.WriteString(fs, 0x04, updateHelper.AutoUpdateID);

            // Server data file location
            if (!string.IsNullOrEmpty(serverFileLoc))
                WriteFiles.WriteString(fs, 0x05, serverFileLoc);

            // Client's server file location (self update server file)
            if (!string.IsNullOrEmpty(clientSFLoc))
                WriteFiles.WriteString(fs, 0x06, clientSFLoc);

            // temp directory
            if (!string.IsNullOrEmpty(tempDirectory))
                WriteFiles.WriteString(fs, 0x07, tempDirectory);

            // the update filename
            if (!string.IsNullOrEmpty(updateFilename))
                WriteFiles.WriteString(fs, 0x08, updateFilename);

            fs.WriteByte(0xFF);
            fs.Close();
        }

        void LoadAutoUpdateData()
        {
            autoUpdateStateFile = Path.Combine(tempDirectory, "autoupdate");

            using (FileStream fs = new FileStream(autoUpdateStateFile, FileMode.Open, FileAccess.Read))
            {
                if (!ReadFiles.IsHeaderValid(fs, "IUAUFV1"))
                {
                    throw new Exception("Auto update state file ID is wrong.");
                }

                byte bType = (byte) fs.ReadByte();
                while (!ReadFiles.ReachedEndByte(fs, bType, 0xFF))
                {
                    switch (bType)
                    {
                        case 0x01:

                            startStep = (UpdateStepOn) ReadFiles.ReadInt(fs);

                            break;
                        //case 0x02:

                            //TODO: use the DateTime for something

                            //break;
                        case 0x03: // file to execute
                            updateHelper.FileToExecuteAfterUpdate = ReadFiles.ReadString(fs);
                            break;

                        case 0x04: // autoupdate ID
                            updateHelper.AutoUpdateID = ReadFiles.ReadString(fs);
                            break;

                        case 0x05: // Server data file location
                            serverFileLoc = ReadFiles.ReadString(fs);

                            if (!File.Exists(serverFileLoc))
                                serverFileLoc = null;

                            break;

                        case 0x06: // Client's server file location (self update server file)
                            clientSFLoc = ReadFiles.ReadString(fs);

                            if (!File.Exists(clientSFLoc))
                                clientSFLoc = null;
                            break;

                        case 0x07: // Temp directory
                            tempDirectory = ReadFiles.ReadString(fs);
                            break;

                        case 0x08: // update filename
                            updateFilename = ReadFiles.ReadString(fs);
                            break;

                        default:
                            ReadFiles.SkipField(fs, bType);
                            break;
                    }

                    bType = (byte) fs.ReadByte();
                }
            }

            // if the server file doesn't exist we need to download a new one
            if (serverFileLoc == null)
                startStep = UpdateStepOn.Checking;
            else
            {
                // load the server file
                LoadServerFile(true);
            }
        }

    }
}