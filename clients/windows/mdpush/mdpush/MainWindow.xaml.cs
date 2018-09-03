/*
BSD 3-Clause License

Copyright(c) 2018, AARNet
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

* Redistributions of source code must retain the above copyright notice, this
  list of conditions and the following disclaimer.

* Redistributions in binary form must reproduce the above copyright notice,
  this list of conditions and the following disclaimer in the documentation
  and/or other materials provided with the distribution.

* Neither the name of the copyright holder nor the names of its
  contributors may be used to endorse or promote products derived from
  this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED.IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.WindowsAPICodePack.Dialogs;
using System.IO;
using System.Net;
using System.ComponentModel;
using System.Windows.Threading;
using System.Security.Cryptography;
using System.Windows.Media.Animation;
using System.Runtime.CompilerServices;
using System.Reflection;
using Microsoft.Research.DynamicDataDisplay;
using Microsoft.Research.DynamicDataDisplay.Common;
using Microsoft.Research.DynamicDataDisplay.DataSources;
using System.Web.Script.Serialization;
using ToolStackCRCLib;
using System.Diagnostics;
using System.Windows.Input;
using Jobs;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace mdpush
{
    public class uploadData
    {
        public string options = "";
        public byte[] data = null;
        public int jobid = 0;
        public long size = 0;

        public uploadData(long size)
        {
            Clear(size);
        }
        
        ~uploadData()
        {
            this.data = null;
        }

        public uploadData(string options, byte[] data, int jobid, long size)
        {
            this.options = options;
            this.data = data;
            this.jobid = jobid;
            this.size = size;
        }

        public void Clear(long size)
        {
            this.options = "";
            this.data = new byte[size];
            this.jobid = 0;
            this.size = size;
        }

        public long Length()
        {
            //return this.data.Length;
            return this.size;
        }
        
        /*public uploadData Clone()
        {
            return new uploadData(this.options, (byte[])this.data.Clone(), this.jobid, this.size);
        }*/
    }

    public class uploadDataResult
    {
        public string output { get; set; }
        public long from { get; set; }
        public long to { get; set; }
        public long size { get; set; }
        public string filename { get; set; }
        public bool success { get; set; }
    }

    public class uploadDataResults
    {
        public List<uploadDataResult> chunks { get; set; }
        public long totaluploadsize { get; set; }
    }

    public class uploadFile
    {
        public long size { get; set; }
        public long uploaded { get; set; }
        
        public Adler32 checksum = new Adler32();
        public byte firstbyte = 0;
        public byte retries = 0;

        public uploadFile(long size)
        {
            this.size = size;
            uploaded = 0;
        }

        public int getPercent()
        {
            if (size == 0)
                return 100;
            return (int)(Math.Round(uploaded*100.0 / size));
        }
    }

    public class WorkerResult
    {
        public int jobid = 0;
        public ulong length = 0;

        public WorkerResult(int jobid, ulong length)
        {
            this.jobid = jobid;
            this.length = length;
        }

        public WorkerResult(int jobid, long length)
        {
            this.jobid = jobid;
            this.length = Convert.ToUInt64(length);
        }
    }

    public class SpeedPointCollection : RingArray<SpeedPoint>
    {
        private const int TOTAL_POINTS = 300;
        public SpeedPointCollection() : base(TOTAL_POINTS) { }
    }

    public class SpeedPoint
    {
        public long Date { get; set; }
        public double Speed { get; set; }

        public SpeedPoint(double speed)
        {
            this.Date = new DateTimeOffset(DateTime.Now).ToUnixTimeSeconds();
            this.Speed = speed;
        }
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private long chunksizeMax; //from settings = 100 * 1000000; //*1mb
        private int parallelMax; //from settings = 8;
        private int dataQueueMax; //from settings = 4;
        private int maxFilesPerChunk; //from settings = 10;
        private int maxRetries; //from settings = 5;
        private int sleepTime = 50;
        private int sleepTimeFast = 10;
        private int sleepTimeSlow = 100;
        public string mdpushURL;
        private bool calculateChecksums;

        private byte[] entropy = { 6, 1, 0, 1, 9, 8, 4 }; //for password

        private bool pushing = false;
        private bool stopping = false;
        private string fromDir = "";
        private string logFile = "";
        private string toDir = "";
        public string user = "";
        public string pass = "";
        private ulong totalSize = 0;
        private ulong totalSizePushed = 0;
        private string lastmessage = "";
        private long lastspeedupdate = 0;
        private Queue<string> logsList = new Queue<string>();
        private string exedir = System.AppDomain.CurrentDomain.BaseDirectory;

        private long completedFilesCount = 0;
        private long failedFilesCount = 0;
        private long failedFilesReadCount = 0;
        private Dictionary<string, uploadFile> files = new Dictionary<string, uploadFile>();
        private List<string> failedFiles = new List<string>();

        //private BackgroundWorker[] uploadTasks;
        private Queue<uploadData> dataQueue = new Queue<uploadData>();

        private BackgroundWorker bwDataCollector;
        private BackgroundWorker bwDataUploader = new BackgroundWorker();
        private DateTime StartTime;

        public SpeedPointCollection speedPointCollection;

        private Jobs.Jobs uploadJobs;

        private OptionsWindow optionsWindow;
        private BrowseWindow browseWindow;

        public event PropertyChangedEventHandler PropertyChanged;

        private void RaisePropertyChanged([CallerMemberName] string propName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }

        public static void DoEvents()
        {
            try
            {
                Application.Current.Dispatcher.Invoke(DispatcherPriority.Background, new Action(delegate { }));
            }
            catch
            {
                //nothing
            }
        }

        public String ToDir
        {
            get { return this.toDir; }
            set
            {
                if (this.toDir != value)
                {
                    this.toDir = value;
                    RaisePropertyChanged();
                }
            }
        }

        public String User
        {
            get { return this.user; }
            set
            {
                if (this.user != value)
                {
                    this.user = value;
                    RaisePropertyChanged();
                }
            }
        }

        public String Pass
        {
            get { return this.pass; }
            set
            {
                if (this.pass != value)
                {
                    this.pass = value;
                    password.Password = value;
                    password.GetType().GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(password, new object[] { value.Length, 0 });
                }
            }
        }

        public string FormatBytes(ulong bytes)
        {
            string[] Suffix = { "B", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };
            int i;
            double dblSByte = bytes;
            for (i = 0; i < Suffix.Length && bytes >= 1024; i++, bytes /= 1024)
            {
                dblSByte = bytes / 1024.0;
            }
            return String.Format("{0:0.##} {1}", dblSByte, Suffix[i]);
        }
        public string FormatBytes(double bytes)
        {
            try
            {
                return FormatBytes(Convert.ToUInt64(Math.Round(bytes)));
            }
            catch
            {
                return "";
            }
        }

        public MainWindow()
        {
            ServicePointManager.DefaultConnectionLimit = 100;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.CheckCertificateRevocationList = false;

            InitializeComponent();
            DataContext = this;

            browseWindow = new BrowseWindow(this);

            User = Properties.Settings.Default.username;
            if (Properties.Settings.Default.key != "")
            {
                Pass = DecryptPassword(Properties.Settings.Default.key);
                rememberUser.IsChecked = true;
            }
            ToDir = Properties.Settings.Default.to;
            fromDir = Properties.Settings.Default.from;
            logFile = Properties.Settings.Default.logFile;

            if (logFile == "")
            {
                logFile = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)+"\\rocketLog.txt";
                Properties.Settings.Default.logFile = logFile;
                Properties.Settings.Default.Save();
            }

            UpdateUploadLabel();

            ChangeBox(User, username);
            ChangeBox(Pass, password);
            ChangeBox(ToDir, to);
            fromLabel.Content = fromDir;
            logFileLabel.Content = logFile;

            speedPointCollection = new SpeedPointCollection();
            var ds = new EnumerableDataSource<SpeedPoint>(speedPointCollection);
            ds.SetXMapping(x => x.Date);
            ds.SetYMapping(y => y.Speed);
            LineGraph line = new LineGraph(ds);
            line.LinePen = new System.Windows.Media.Pen(new SolidColorBrush(Color.FromRgb(255, 147, 2)), 1);
            plotter.Children.Add(line);
            plotter.FitToView();
            plotter.AxisGrid.Visibility = Visibility.Hidden;
            speedPointCollection.Add(new SpeedPoint(0));

            ChangeButton(fromDir == "", fromButton);
            ChangeButton(ToDir == "", toButton);
            ChangeButton(logFile == "", logFileButton);
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            Application.Current.Shutdown();
        }

        private void FromButton_Click(object sender, RoutedEventArgs e)
        {
            if (!pushing)
            {
                var dialog = new CommonOpenFileDialog();
                dialog.IsFolderPicker = true;
                if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
                {
                    fromDir = Directory.Exists(dialog.FileName) ? dialog.FileName : System.IO.Path.GetDirectoryName(dialog.FileName);
                    if (fromDir != "")
                    {
                        fromLabel.Content = fromDir;
                        ChangeButton(false, fromButton);
                    }
                }
            }
        }

        private void logFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (!pushing)
            {
                var dialog = new SaveFileDialog()
                {
                    Filter = "Text Files(*.txt)|*.txt|All(*.*)|*"
                };

                if (dialog.ShowDialog() == true)
                {
                    logFile = dialog.FileName;
                    if (logFile != "")
                    {
                        logFileLabel.Content = logFile;
                        ChangeButton(false, logFileButton);
                        Properties.Settings.Default.logFile = logFile;
                        Properties.Settings.Default.Save();
                    }
                }
            }
        }

        public void AddToLog(string text, bool updateText = true)
        {
            if (text != "")
            {
                logsList.Enqueue(text);
                while (logsList.Count > 100)
                {
                    logsList.Dequeue();
                }
            }
            if (updateText)
            {
                log.Text = string.Join("\n", logsList);
                log.ScrollToEnd();
            }
            if (logFile != "" && text.Trim() != "")
            {
                using (StreamWriter w = File.AppendText(logFile))
                {
                    string dt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ff")+" ";
                    text = dt + text.TrimEnd().Replace("\n","\n"+dt);
                    w.WriteLine(text);
                }
            }
        }

        public void UpdateUploadLabel()
        {
            uploadLabel.Content = FormatBytes(Properties.Settings.Default.chunkSize) + " chunks x " + Properties.Settings.Default.parallelUploads + " parallel uploads, with a buffer of " + Properties.Settings.Default.dataQueue + " (~" + FormatBytes((Properties.Settings.Default.dataQueue + Properties.Settings.Default.parallelUploads) * Properties.Settings.Default.chunkSize) + " RAM)";
            Arrows.Width = 10 * Properties.Settings.Default.parallelUploads;
        }

        private string EncryptPassword(string pass)
        {
            byte[] plaintext = Encoding.UTF8.GetBytes(pass);
            byte[] ciphertext = ProtectedData.Protect(plaintext, entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(ciphertext);
        }

        private string DecryptPassword(string cipherpass)
        {
            byte[] ciphertext = Convert.FromBase64String(cipherpass);
            byte[] plaintext = ProtectedData.Unprotect(ciphertext, entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plaintext);
        }

        private void uploadJobs_OnJobDone(object source, JobsEventArgs e)
        {
            try
            {
                this.Dispatcher.Invoke(async () =>
                {
                    JobEventArgs j = e.getJobEventArgs();
                    if (j != null)
                    {
                        Debug.WriteLine(j.getID().ToString() + ": ExitCode: " + j.getExitCode().ToString()
                                        + "\n    stdout: " + j.getStdout().TrimEnd()
                                        + (j.getStderr().TrimEnd() != "" ? ("\n    stderr: " + j.getStderr().TrimEnd()) : ""));

                        if (j.getStdout().TrimEnd() != "")
                        {
                            uploadDataResults results = new JavaScriptSerializer().Deserialize<uploadDataResults>(j.getStdout().TrimEnd());

                            string label = "";

                            string completedFilesList = "";

                            if (files.Count() > 0)
                            {
                                foreach (uploadDataResult chunk in results.chunks)
                                {
                                    if (chunk.success)
                                    {
                                        long uploadedbytes = chunk.to - chunk.from;
                                        files[chunk.filename].uploaded += uploadedbytes;
                                        totalSizePushed += (ulong)uploadedbytes;

                                        if (files[chunk.filename].uploaded >= files[chunk.filename].size) //file complete
                                        {
                                            completedFilesList += ",{\"filename\":\"" + chunk.filename + "\",\"size\":\"" + files[chunk.filename].size + "\",\"firstbyte\":\"" + files[chunk.filename].firstbyte + "\",\"checksum\":\"" + (calculateChecksums ? String.Format("{0:X}", files[chunk.filename].checksum.adler()) : "-1") + "\"}";
                                            completedFilesCount++;
                                            files.Remove(chunk.filename);
                                        }
                                    }

                                    AddToLog((chunk.success ? "SUCCESS  " : "ERROR    ") + chunk.filename + " " + chunk.from + " to " + chunk.to + " of " + chunk.size, false);

                                    UpdateFileUploadList();

                                    if (chunk.output != null && chunk.output.Trim() != "") AddToLog(chunk.output.Trim(), false);
                                }
                            }

                            if (completedFilesList != "")
                            {
                                string output = await FilesCompleteAsync(completedFilesList.Substring(1));
                                AddToLog(output, false);
                            }

                            TimeSpan span = DateTime.Now.Subtract(StartTime);
                            label = FormatBytes(totalSizePushed) + " in " + span.ToString().Substring(0, span.ToString().IndexOf("."));
                            double speed = totalSizePushed / span.TotalSeconds;
                            UpdateStats(label, speed);
                            AddToLog("", true);
                        }
                    }
                    else
                    {
                        Debug.WriteLine("j is null, this should not happen");
                    }

                    uploadJobs.allJobs()[j.getID()].SetBusy(false);
                });
            }
            catch
            {
                //nothing
            }
        }
        
        /*private byte[] ReadFile(string filename, long offset, long length)
        {
            Debug.WriteLine("READING FILE: " + filename + " " + offset.ToString() + " to " + length.ToString());
            using (BinaryReader b = new BinaryReader(File.Open(filename, FileMode.Open)))
            {
                b.BaseStream.Seek(offset, SeekOrigin.Begin);
                //return new string(b.ReadChars(length));
                //return System.Text.Encoding.ASCII.GetString(b.ReadBytes(length));
                byte[] data = b.ReadBytes((int)length);
                Debug.WriteLine("READING DONE: " + filename + " " + offset.ToString() + " to " + length.ToString());
                return data;
            }            
        }*/

        private byte ReadFile(string fileName, long fileOffset, long fileLength, ref byte[] data, int dataOffset)
        {
            Debug.WriteLine("READING FILE: " + fileName + " " + fileOffset.ToString() + " to " + fileLength.ToString());
            try
            {
                using (FileStream fsSource = new FileStream(fileName, FileMode.Open, FileAccess.Read))
                {
                    fsSource.Seek(fileOffset, SeekOrigin.Begin);
                    fsSource.Read(data, dataOffset, (int)fileLength);
                    Debug.WriteLine("READING DONE: " + fileName + " " + fileOffset.ToString() + " to " + fileLength.ToString());
                    return data[dataOffset];
                }
            }
            catch (FileNotFoundException ioEx)
            {
                Debug.WriteLine(ioEx.Message);
            }
            return 0;
        }

        /*private string ScanOC(string dir)
        {
            try
            {
                WebRequest httpWebRequest = WebRequest.Create(mdpushURL + "scan.php");
                httpWebRequest.ContentType = "application/json";
                httpWebRequest.Method = "POST";
                httpWebRequest.Timeout = Timeout.Infinite;
                if (Properties.Settings.Default.proxy == 1)
                {
                    httpWebRequest.Proxy = null;
                }
                else if (Properties.Settings.Default.proxy == 2)
                {
                    httpWebRequest.Proxy = new WebProxy(Properties.Settings.Default.proxyHost, Int32.Parse(Properties.Settings.Default.proxyPort));
                }
                using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream()))
                {
                    streamWriter.Write(user + "\n" + pass + "\n{\"path\":\"" + dir + "\"}\n");
                    streamWriter.Flush();
                    streamWriter.Close();
                }
                using (var streamReader = new StreamReader(httpWebRequest.GetResponse().GetResponseStream()))
                {
                    return streamReader.ReadToEnd().Trim();
                }
            } catch (Exception exception)
            {
                return "ERROR: Web error! "+ exception.Message;
            }
        }*/

        private async Task<string> FilesCompleteAsync(string list)
        {
            try
            {
                WebRequest httpWebRequest = WebRequest.Create(mdpushURL + "filescomplete.php");
                httpWebRequest.ContentType = "application/json";
                httpWebRequest.Method = "POST";
                httpWebRequest.Timeout = Timeout.Infinite;
                if (Properties.Settings.Default.proxy == 1)
                {
                    httpWebRequest.Proxy = null;
                }
                else if (Properties.Settings.Default.proxy == 2)
                {
                    httpWebRequest.Proxy = new WebProxy(Properties.Settings.Default.proxyHost, Int32.Parse(Properties.Settings.Default.proxyPort));
                }
                using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream()))
                {
                    streamWriter.Write(user + "\n" + pass + "\n{\"files\":[" + list + "]}\n");
                    streamWriter.Flush();
                    streamWriter.Close();
                }

                using (var response = await httpWebRequest.GetResponseAsync())
                using (var streamReader = new StreamReader(response.GetResponseStream()))
                {
                    string output = streamReader.ReadToEnd().Trim();
                    //Debug.WriteLine(list);
                    using (StringReader reader = new StringReader(output))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (line.IndexOf(" for file ") != -1)
                            {
                                string filename = line.Substring(line.IndexOf(" for file ") + 11);
                                failedFiles.Add(filename);
                                completedFilesCount--;
                                failedFilesCount++;
                                //Debug.WriteLine("MD FAILED: " + filename);
                                //Debug.WriteLine("MD FAILED count: " + failedFiles.Count);
                            }
                        }
                    }
                    return output;
                }
            }
            catch (Exception exception)
            {
                return "ERROR: Web error! " + exception.Message;
            }
        }

        private string reportStats(ulong totalSize, double totalSeconds, long completedFilesCount, long failedFilesCount, long failedFilesReadCount)
        {
            string json = "{" +
                            "\"time\":\"" + totalSeconds + "\"," +
                            "\"size\":\"" + totalSize + "\"," +
                            "\"completedFiles\":\"" + completedFilesCount + "\"," +
                            "\"failedFiles\":\"" + failedFilesCount + "\"," +
                            "\"failedReadFiles\":\"" + failedFilesReadCount + "\"," +
                            "\"chunkSize\":\"" + Properties.Settings.Default.chunkSize + "\"," +
                            "\"parallel\":\"" + Properties.Settings.Default.parallelUploads + "\"," +
                            "\"buffer\":\"" + Properties.Settings.Default.dataQueue + "\"" +
                          "}";

            try
            {
                WebRequest httpWebRequest = WebRequest.Create(mdpushURL + "report.php");
                httpWebRequest.ContentType = "application/json";
                httpWebRequest.Method = "POST";
                httpWebRequest.Timeout = Timeout.Infinite;
                if (Properties.Settings.Default.proxy == 1)
                {
                    httpWebRequest.Proxy = null;
                }
                else if (Properties.Settings.Default.proxy == 2)
                {
                    httpWebRequest.Proxy = new WebProxy(Properties.Settings.Default.proxyHost, Int32.Parse(Properties.Settings.Default.proxyPort));
                }
                using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream()))
                {
                    streamWriter.Write(user + "\n" + pass + "\n" + json + "\n");
                    streamWriter.Flush();
                    streamWriter.Close();
                }
                using (var streamReader = new StreamReader(httpWebRequest.GetResponse().GetResponseStream()))
                {
                    return streamReader.ReadToEnd().Trim();
                }
            }
            catch (Exception exception)
            {
                return "ERROR: Web error! " + exception.Message;
            }
        }

        private void AddToQueue(BackgroundWorker worker, ref uploadData data, long chunksize)
        {
            if (stopping)
                return;

            data.size = chunksize; //in case its smaller than max

            while (dataQueue.Count()>=dataQueueMax)
            {
                //worker.ReportProgress(0, "  Data Buffer is full, waiting for it to empty a little");
                //Debug.WriteLine("Data Buffer is full, waiting for it to empty a little");
                System.Threading.Thread.Sleep(sleepTime);
                DoEvents();
            }
            dataQueue.Enqueue(data);
            worker.ReportProgress(0, "");
            Debug.WriteLine("Data added to Data Buffer Size : " + dataQueue.Count() + "/" + dataQueueMax + " (" + FormatBytes(dataQueue.Count() * chunksizeMax) + ")");
        }

        private void BwDataUploader_DoWork(object sender, DoWorkEventArgs e)
        {
            e.Result = new WorkerResult(0, 0);
            BackgroundWorker worker = sender as BackgroundWorker;

            while (pushing)
            {
                if (dataQueue.Count > 0)
                {
                    foreach (Job j in uploadJobs.allJobs())
                    {
                        if (!j.IsBusy())
                        {
                            uploadData data = dataQueue.Dequeue();

                            string cmd = "mdpushWorker.exe";

                            //using (BinaryWriter test = new BinaryWriter(new FileStream("C:\\Users\\michael.dsilva\\Desktop\\test\\chunk"+ (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds.ToString()+".bin", FileMode.CreateNew)))
                            using (BinaryWriter streamWriter = new BinaryWriter(j.RunJob(exedir, cmd, "").BaseStream))
                            {
                                string header = user + "\n" + pass + "\n{\"chunks\":[" + data.options.Substring(1) + "]}\n";

                                streamWriter.Write(header.ToCharArray());
                                streamWriter.Write(data.data.ToArray());

                                //test.Write(header.ToCharArray());
                                //test.Write(data.data.ToArray());

                                Debug.WriteLine("Data removed from Data Buffer Size : " + dataQueue.Count() + "/" + dataQueueMax + " (" + FormatBytes(dataQueue.Count() * chunksizeMax) + ")");
                                Debug.WriteLine("  UPLOAD NEW TASK : " + data.options);
                                Debug.WriteLine("  " + j.GetId().ToString() + ": " + exedir + cmd);

                                data.data = null;
                                data = null;
                                streamWriter.Flush();
                                streamWriter.Close();

                                //test.Close();
                            }
                            break;
                        }
                    }
                }
                else
                {
                    //if (pushing) Debug.WriteLine("Data Buffer is empty, waiting for it to fill a little");
                    System.Threading.Thread.Sleep(sleepTimeFast);
                    //DoEvents();
                }
            }

            while (uploadJobs.jobsBusy()>0)
            {
                System.Threading.Thread.Sleep(sleepTimeFast);
            }
        }

        private bool IsFileLocked(FileInfo file)
        {
            FileStream stream = null;

            try
            {
                stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.None);
            }
            catch (Exception)
            {
                //the file is unavailable because it is:
                //still being written to
                //or being processed by another thread
                //or does not exist (has already been processed)
                return true;
            }
            finally
            {
                if (stream != null)
                    stream.Close();
            }

            //file is not locked
            return false;
        }

        private IEnumerable<string> GetDirectoryFiles(BackgroundWorker worker, string rootPath, string patternMatch, SearchOption searchOption)
        {
            if (stopping)
                return null;

            var foundFiles = Enumerable.Empty<string>();
            bool fail = false;

            if (searchOption == SearchOption.AllDirectories)
            {
                try
                {
                    IEnumerable<string> subDirs = Directory.EnumerateDirectories(rootPath);
                    foreach (string dir in subDirs)
                    {
                        DoEvents();
                        var subDirList = GetDirectoryFiles(worker, dir, patternMatch, searchOption);
                        if (subDirList!=null)
                            foundFiles = foundFiles.Concat(subDirList); // Add files in subdirectories recursively to the list
                    }
                }
                catch (UnauthorizedAccessException) { fail = true; }
                catch (PathTooLongException) { fail = true; }
            }

            try
            {
                foundFiles = foundFiles.Concat(Directory.EnumerateFiles(rootPath, patternMatch)); // Add files from the current directory
            }
            catch (UnauthorizedAccessException) { fail = true; }

            if (fail)
            {
                failedFilesReadCount++;
                worker.ReportProgress(0, "ERROR: Can not process " + rootPath + " access is denied");
            }

            return foundFiles;
        }

        private void ProcessFileList(DoWorkEventArgs e, BackgroundWorker worker, string directory, IEnumerable<string> fileList)
        {
            if (stopping)
                return;

            totalSize = 0;
            long chunksize = 0;
            uploadData data = new uploadData(chunksizeMax);

            //DirectoryInfo dir = null;
            int filesinchunk = 0;
            if (directory != "")
            {
                //dir = new DirectoryInfo(directory);
                //fileList = dir.EnumerateFiles("*", SearchOption.AllDirectories);
                fileList = GetDirectoryFiles(worker, directory, "*", SearchOption.AllDirectories);
            }

            if (fileList == null || stopping)
                return;

            StartTime = DateTime.Now;
            totalSizePushed = 0;

            foreach (var fileName in fileList)
            {
                if (worker.CancellationPending)
                {
                    e.Cancel = true;
                    return;
                }
                if (stopping)
                    return;

                var file = new FileInfo(fileName);
                if (IsFileLocked(file))
                {
                    worker.ReportProgress(0, "ERROR: Can not process " + file.FullName + " another process/application is using it!");
                    continue;
                }

                filesinchunk++;
                if (filesinchunk > maxFilesPerChunk && chunksize > 0)
                {
                    AddToQueue(worker, ref data, chunksize);
                    //worker.ReportProgress(0, "DEBUG: 1 " + data.options);

                    data = new uploadData(chunksizeMax);
                    chunksize = 0;
                    filesinchunk = 0;
                }

                long offset = 0;
                totalSize += Convert.ToUInt64(file.Length);
                string toFile = (toDir + file.FullName.Substring(fromDir.Length)).Replace("\\", "/");

                files.Add(toFile, new uploadFile(file.Length));

                worker.ReportProgress(0, "PROCESSING " + file.FullName + "\n        -> " + toFile + " (" + FormatBytes(Convert.ToUInt64(file.Length)) + ")");

                while (file.Length - offset > chunksizeMax - chunksize && !worker.CancellationPending)
                {
                    long difference = chunksizeMax - chunksize;
                    data.options += ",{\"filename\":\"" + toFile + "\",\"offset\":" + offset + ",\"chunksize\":" + difference + ",\"size\":" + file.Length + "}";

                    byte firstbyte = ReadFile(file.FullName, offset, difference, ref data.data, (int)chunksize);
                    if (calculateChecksums) files[toFile].checksum.addToAdler(ref data.data, (int)difference, (UInt32)chunksize);
                    if (offset == 0)
                        files[toFile].firstbyte = firstbyte;

                    offset += chunksizeMax - chunksize;

                    AddToQueue(worker, ref data, chunksize);

                    data = new uploadData(chunksizeMax);
                    chunksize = 0;
                    filesinchunk = 0;
                }
                if ((file.Length == 0 || file.Length - offset > 0) && !worker.CancellationPending)
                {
                    long difference = file.Length - offset;
                    data.options += ",{\"filename\":\"" + toFile + "\",\"offset\":" + offset + ",\"chunksize\":" + difference + ",\"size\":" + file.Length + "}";

                    if (file.Length == 0)
                    {
                        Debug.WriteLine("READING FILE: " + file.FullName + " 0 bytes");
                        files[toFile].firstbyte = 0;
                    }
                    else
                    {
                        byte firstbyte = ReadFile(file.FullName, offset, difference, ref data.data, (int)chunksize);
                        if (calculateChecksums) files[toFile].checksum.addToAdler(ref data.data, (int)difference, (UInt32)chunksize);
                        if (offset == 0)
                            files[toFile].firstbyte = firstbyte;
                    }
                    offset += difference;
                    chunksize += difference;

                    if (chunksize >= chunksizeMax)
                    {
                        AddToQueue(worker, ref data, chunksize);
                        //worker.ReportProgress(0, "DEBUG: 3 " + data.options);

                        data = new uploadData(chunksizeMax);
                        chunksize = 0;
                        filesinchunk = 0;
                    }
                }

                //Debug.WriteLine("ADLER32  " + toFile + " " + String.Format("{0:X}", files[toFile].checksum.adler()));
            }
            if (chunksize > 0 && !worker.CancellationPending)
            {
                AddToQueue(worker, ref data, chunksize);
                //worker.ReportProgress(0, "DEBUG: 4 " + data.options);
            }
        }

        private void WaitForUploadsToEnd(BackgroundWorker worker)
        {
            //wait for dataQueue to end
            System.Threading.Thread.Sleep(sleepTimeSlow);
            DoEvents();
            while (dataQueue.Count() > 0)
            {
                worker.ReportProgress(0, "  Winding down, waiting on Data Buffer to empty: " + dataQueue.Count() + "/" + dataQueueMax + " (" + FormatBytes(dataQueue.Count() * chunksizeMax) + ")");
                System.Threading.Thread.Sleep(sleepTimeSlow);
                DoEvents();
            }
            worker.ReportProgress(0, "  Data Buffer empty");
            //wait for uploads to end
            while (uploadJobs.jobsBusy() > 0)
            {
                System.Threading.Thread.Sleep(sleepTimeSlow);
                DoEvents();
            }
            //wait for files to complete
            worker.ReportProgress(0, "  Waiting for files to complete");
            while (files.Count() > 0)
            {
                System.Threading.Thread.Sleep(sleepTimeSlow);
                DoEvents();
            }
            System.Threading.Thread.Sleep(sleepTime);
            DoEvents();
        }

        private void BwDataCollector_DoWork(object sender, DoWorkEventArgs e)
        {
            e.Result = new WorkerResult(0, 0);
            BackgroundWorker worker = sender as BackgroundWorker;

            ProcessFileList(e,worker,fromDir,null);
            WaitForUploadsToEnd(worker);

            //retry failed files           
            int retry = 1;
            while (failedFiles.Count > 0 && retry <= maxRetries)
            {
                if (stopping)
                    break;

                worker.ReportProgress(0, "FAILED Uploads: " + failedFiles.Count);
                worker.ReportProgress(0, "RETRY : " + retry + "/" + maxRetries);

                List<string> fileList = new List<string>();
                foreach (string failedFile in failedFiles)
                {
                    if (stopping)
                        break;
                    string fromFile = fromDir + "\\" + failedFile.Substring(toDir.Length).Replace("/", "\\");
                    fileList.Add(fromFile);
                }
                failedFiles.Clear();

                ProcessFileList(e, worker, "", fileList);
                WaitForUploadsToEnd(worker);
                retry++;
            }
            if (failedFiles.Count > 0) //give up
            {
                worker.ReportProgress(0, "FAILED Uploads: " + failedFiles.Count);
                worker.ReportProgress(0, "====================\nThe following files failed to upload");
                foreach (string failedFile in failedFiles)
                {
                    if (stopping)
                        break;

                    string fromFile = fromDir + "\\" + failedFile.Substring(toDir.Length).Replace("/", "\\");
                    worker.ReportProgress(0, "  " + fromFile + " -> " + failedFile);
                }
            }

            pushing = false;
            while (bwDataUploader.IsBusy)
            {
                worker.ReportProgress(0, "  Waiting for last few uploads to complete...");
                System.Threading.Thread.Sleep(sleepTimeSlow);
                DoEvents();
            }

            //scan files on OC
            //worker.ReportProgress(0, "====================\nScanning new files...");
            //worker.ReportProgress(0, ScanOC(toDir));

            //worker.ReportProgress(0, "====================\nUpload Complete\nFile(s) will appear in CloudStor shortly");

            worker.ReportProgress(100, "");

            //win7 GC.Collect();

            if (worker.CancellationPending)
            {
                e.Cancel = true;
            }            
        }

        private void UpdateFileUploadList()
        {
            string output = "Completed Uploads    " + completedFilesCount + " files\n";
            if (failedFilesCount > 0)
                output += "Failed Attempts      " + failedFilesCount + " files\n";
            if (failedFilesReadCount > 0)
                output += "Failed to Open/Read  " + failedFilesReadCount + " files/folders\n";
            if (files.Count > 0)
                output += "Partial Uploads      " + files.Count() + " files\n";
            if (files.Count <= 20)
            {
                try
                {
                    foreach (KeyValuePair<string, uploadFile> file in files)
                    {
                        output += "  " + file.Value.getPercent().ToString().PadLeft(3, ' ') + "%  " + file.Key + "\n";
                    }
                }
                catch
                {
                    output += "  Files uploading faster than we can track safely!";
                }
            }
            else
            {
                output += "  Files uploading faster than we can track safely!";
            }
            outputlog.Text = output;
            DoEvents();
        }

        private void UpdateStats(string label, double speed)
        {
            if (label != "")
            {
                statLabel.Text = label;
            }

            if (speed > -1)
            {
                if (new DateTimeOffset(DateTime.Now).ToUnixTimeSeconds() - lastspeedupdate >= 1)
                {
                    lastspeedupdate = new DateTimeOffset(DateTime.Now).ToUnixTimeSeconds();
                    speedPointCollection.Add(new SpeedPoint(Math.Round(speed)));
                }
                speedLabel.Text = FormatBytes(speed) + "/s\n" + FormatBytes(speed * 8) + "its/s";
            }

            DataQueueProgressBar.Value = dataQueue.Count();
            DataQueueProgressText.Content = dataQueue.Count() + " (" + FormatBytes(dataQueue.Count() * chunksizeMax) + ")";

            int slotsInUse = uploadJobs.jobsBusy();
            UploadProgressBar.Value = slotsInUse;
            UploadProgressText.Content = slotsInUse;
        }
        private void UpdateStats(string label) { UpdateStats(label, -1); }

        private void Bw_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            WorkerResult result = e.Result as WorkerResult;
            string text = "";
            string label = "";

            if ((e.Cancelled == true))
            {
                text = "  Job: " + result.jobid + " Canceled!";
            }
            else if (!(e.Error == null))
            {
                text = "  Job: " + result.jobid + " Error: " + e.Error.Message;
            }
            /*else
            {
                if (result.jobid > 0)
                {
                    text = "  Job: " + result.jobid + " Ended";

                    totalSizePushed += result.length;
                    TimeSpan span = DateTime.Now.Subtract(StartTime);
                    label = FormatBytes(totalSizePushed) + " in " + span.ToString().Substring(0, span.ToString().IndexOf("."));
                    speed = totalSizePushed / span.TotalSeconds;
                }
            }*/

            try
            { 
                if (Application.Current != null) Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => {
                    try
                    {
                        this.Dispatcher.Invoke(() =>
                        {
                            if (text != "")
                            {
                                AddToLog(text);
                            }

                            UpdateStats(label);
                            UpdateFileUploadList();
                        });
                    }
                    catch
                    {
                        //nothing
                    }
                }));
            }
            catch
            {
                //nothing
            }

            //win7 GC.Collect();
        }

        private void Bw_DataCollectorProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            try
            {
                if (Application.Current != null) Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                {
                    try
                    {
                        this.Dispatcher.Invoke(() =>
                        {
                            string message = (string)e.UserState;
                            string label = "";

                            if (lastmessage != message)
                            {
                                AddToLog(message);
                                lastmessage = message;
                            }

                            if (e.ProgressPercentage == 100)
                            {
                                TimeSpan span = DateTime.Now.Subtract(StartTime);
                                AddToLog("====================\nUpload Complete" + (stopping?" (stopped transfer)":"") + "\nFile(s) will appear in CloudStor shortly\n====================\n" + FormatBytes(totalSize) + " in " + span.ToString().Substring(0, span.ToString().IndexOf(".")) + " " + FormatBytes(totalSize / span.TotalSeconds) + "/s (" + FormatBytes(totalSize / span.TotalSeconds * 8) + "its/s) processed\n====================");
                                label = FormatBytes(totalSize) + " in " + span.ToString().Substring(0, span.ToString().IndexOf(".")) + " " + FormatBytes(totalSize / span.TotalSeconds) + "/s [" + FormatBytes(totalSize / span.TotalSeconds * 8) + "its/s] processed\n";

                                AddToLog(reportStats(totalSize, span.TotalSeconds, completedFilesCount, failedFilesCount, failedFilesReadCount));

                                pushing = false;
                                stopping = false;
                                PushButton.Content = "Push";

                                TimeSpan t = TimeSpan.FromSeconds(0.2);
                                ColorAnimation a = new ColorAnimation();
                                a.From = (Color)ColorConverter.ConvertFromString("#FF9302");
                                a.To = (Color)ColorConverter.ConvertFromString("#00FF00");
                                a.Duration = t;
                                ColorAnimation b = new ColorAnimation();
                                b.From = a.To;
                                b.To = a.From;
                                b.Duration = t;
                                b.BeginTime = t;

                                Storyboard sb = new Storyboard();
                                sb.Children.Add(a);
                                sb.Children.Add(b);
                                Storyboard.SetTarget(a, PushButton);
                                Storyboard.SetTarget(b, PushButton);
                                Storyboard.SetTargetProperty(a, new PropertyPath("Background.Color"));
                                Storyboard.SetTargetProperty(b, new PropertyPath("Background.Color"));
                                sb.Begin();
                            }
                            log.ScrollToEnd();

                            UpdateStats(label);
                        });
                    }
                    catch
                    {
                        //nothing
                    }
                }));
            }
            catch
            {
                //nothing
            }
        }

        private void Bw_DataUploaderProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            try
            {
                if (Application.Current != null) Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                {
                    try
                    {
                        this.Dispatcher.Invoke(() =>
                        {
                            string message = (string)e.UserState;

                            if (lastmessage != message)
                            {
                                AddToLog(message);
                                lastmessage = message;
                            }

                            UpdateStats("");
                            UpdateFileUploadList();
                        });
                    }
                    catch
                    {
                        //nothing
                    }
                }));
            }
            catch
            {
                //nothing
            }
        }

        private bool CanLogin()
        {
            try
            {
                WebRequest httpWebRequest = WebRequest.Create(mdpushURL + "login.php");
                httpWebRequest.ContentType = "application/json";
                httpWebRequest.Method = "POST";
                httpWebRequest.Timeout = Timeout.Infinite;
                if (Properties.Settings.Default.proxy == 1)
                {
                    httpWebRequest.Proxy = null;
                }
                else if (Properties.Settings.Default.proxy == 2)
                {
                    httpWebRequest.Proxy = new WebProxy(Properties.Settings.Default.proxyHost, Int32.Parse(Properties.Settings.Default.proxyPort));
                }
                using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream()))
                {
                    streamWriter.Write(user + "\n" + pass + "\n{\"path\":\"" + toDir + "\",\"version\":\""+version.Content+"\"}\n");
                    streamWriter.Flush();
                    streamWriter.Close();
                }
                using (var streamReader = new StreamReader(httpWebRequest.GetResponse().GetResponseStream()))
                {
                    string output = streamReader.ReadToEnd().Trim();
                    AddToLog(output);
                    if (output.IndexOf("ERROR")==-1 || output.IndexOf("WARNING") == -1)
                    {
                        return true;
                    }
                }
            }
            catch (Exception exception)
            {
                AddToLog("ERROR: Web Error\n" + exception.Message);
                return false;
            }
            return false;
        }

        private void PushButton_Click(object sender, RoutedEventArgs e)
        {
            if (stopping)
                return;

            if (pushing || (bwDataCollector != null && bwDataCollector.IsBusy == true)) //uploading already
            {
                stopping = true;
                PushButton.Content = "Stopping";

                files.Clear();
                dataQueue.Clear();
                
                return;
            }

            if (fromDir == "")
            {
                AddToLog("Please Select a Folder to Upload.");
                return;
            }

            if (logFile == "")
            {
                AddToLog("Please Select a Log File");
                return;
            }

            toDir = to.Text.Replace("\\", "/");
            if (toDir.EndsWith("/")) toDir = toDir.Substring(0, toDir.Length - 1);
            if (!toDir.StartsWith("/")) toDir = "/" + toDir;
            to.Text = toDir;

            if (toDir == "/")
            {
                AddToLog("Please Select a Folder to upload to. (can not upload to /)");
                return;
            }

            Properties.Settings.Default.username = User;
            Properties.Settings.Default.key = rememberUser.IsChecked.Value ? EncryptPassword(Pass) : "";
            Properties.Settings.Default.to = ToDir;
            Properties.Settings.Default.from = fromDir;
            Properties.Settings.Default.logFile = logFile;
            Properties.Settings.Default.Save();

            files.Clear();
            logsList.Clear();
            outputlog.Text = "";
            log.Text = "";
            statLabel.Text = "";
            speedLabel.Text = "";

            completedFilesCount = 0;
            failedFilesCount = 0;
            chunksizeMax = Properties.Settings.Default.chunkSize;
            parallelMax = Properties.Settings.Default.parallelUploads;
            dataQueueMax = Properties.Settings.Default.dataQueue;
            maxFilesPerChunk = Properties.Settings.Default.maxFilesPerChunk;
            maxRetries = Properties.Settings.Default.maxRetries;
            mdpushURL = "https://" + Properties.Settings.Default.cloudstorURL + "/rocket/";
            calculateChecksums = Properties.Settings.Default.checksum;

            if (toDir == "/Shared")
            {
                AddToLog("Can not upload to /Shared, Please specify a share");
                return;
            }

            if (!CanLogin()) return;

            pushing = true;
            PushButton.Content = "Stop";

            uploadJobs = new Jobs.Jobs(parallelMax);
            uploadJobs.OnJobDone += new  JobsEventHandler(uploadJobs_OnJobDone);

            bwDataUploader.DoWork += new DoWorkEventHandler(BwDataUploader_DoWork);
            bwDataUploader.ProgressChanged += new ProgressChangedEventHandler(Bw_DataUploaderProgressChanged);
            bwDataUploader.RunWorkerCompleted += new RunWorkerCompletedEventHandler(Bw_RunWorkerCompleted);
            bwDataUploader.WorkerReportsProgress = true;
            bwDataUploader.WorkerSupportsCancellation = true;

            bwDataCollector = new BackgroundWorker();
            bwDataCollector.DoWork += new DoWorkEventHandler(BwDataCollector_DoWork);
            bwDataCollector.ProgressChanged += new ProgressChangedEventHandler(Bw_DataCollectorProgressChanged);
            bwDataCollector.RunWorkerCompleted += new RunWorkerCompletedEventHandler(Bw_RunWorkerCompleted);
            bwDataCollector.WorkerReportsProgress = true;
            bwDataCollector.WorkerSupportsCancellation = true;

            bwDataUploader.RunWorkerAsync();

            DataQueueProgressBar.Value = 0;
            DataQueueProgressBar.Maximum = dataQueueMax;
            UploadProgressBar.Value = 0;
            UploadProgressBar.Maximum = parallelMax;
                
            bwDataCollector.RunWorkerAsync();
        }

        private void OptionsButton_Click(object sender, RoutedEventArgs e)
        {
            if (!pushing && (bwDataCollector == null || bwDataCollector.IsBusy != true))
            {
                optionsWindow = new OptionsWindow(this);
                optionsWindow.ShowDialog();
            }
        }

        private void ToButton_Click(object sender, RoutedEventArgs e)
        {
            if (!pushing && (bwDataCollector == null || bwDataCollector.IsBusy != true))
            {
                browseWindow.Reload(false);
                browseWindow.ShowDialog();
            }
        }

        private void ChangeButton(bool on, Button b)
        {
            if (on)
            {
                ColorAnimation ca = new ColorAnimation();
                ca.From = (Color)ColorConverter.ConvertFromString("#FF9302");
                ca.To = (Color)ColorConverter.ConvertFromString("#FF0000");
                ca.Duration = TimeSpan.FromSeconds(1);
                ca.RepeatBehavior = RepeatBehavior.Forever;
                ca.AutoReverse = true;

                Storyboard sb = new Storyboard();
                sb.Children.Add(ca);
                Storyboard.SetTarget(ca, b);
                Storyboard.SetTargetProperty(ca, new PropertyPath("Background.Color"));
                sb.Begin();
            }
            else
            {
                b.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF9302"));
            }
        }

        private bool ChangeBox(string s, Object o)
        {
            if (s.Trim() == "")
            {
                (o as Control).BorderBrush = System.Windows.Media.Brushes.Red;

                ColorAnimation ca = new ColorAnimation();
                ca.From = (Color)ColorConverter.ConvertFromString("#F0F0F0");
                ca.To = (Color)ColorConverter.ConvertFromString("#FF0000");
                ca.Duration = TimeSpan.FromSeconds(1);
                ca.RepeatBehavior = RepeatBehavior.Forever;
                ca.AutoReverse = true;

                Storyboard sb = new Storyboard();
                sb.Children.Add(ca);
                Storyboard.SetTarget(ca, o as DependencyObject);
                Storyboard.SetTargetProperty(ca, new PropertyPath("(Border.BorderBrush).(SolidColorBrush.Color)"));
                sb.Begin();
                return true;
            }
            else
            {
                (o as Control).BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ABADB3"));
                return false;
            }
        }
        private void Textbox_TextChanged(object sender, TextChangedEventArgs e) { ChangeBox((sender as TextBox).Text, sender); }
        private void Password_PasswordChanged(object sender, RoutedEventArgs e)
        {
            ChangeBox((sender as PasswordBox).Password, sender);
            Pass = password.Password;
        }
        private void To_TextChanged(object sender, TextChangedEventArgs e)
        {
            ChangeButton(ChangeBox((sender as TextBox).Text, sender), toButton);
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start("https://support.aarnet.edu.au/hc/en-us/sections/115000264294-CloudStor-Rocket");
        }

        private void AARNetImage_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
            {
                System.Diagnostics.Process.Start("https://www.aarnet.edu.au/");
            }
        }        
    }
}
