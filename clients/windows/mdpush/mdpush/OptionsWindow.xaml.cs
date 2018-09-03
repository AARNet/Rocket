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
using System.Windows;
using System.Net;
using System.IO;
using System.Windows.Threading;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace mdpush
{
    public class uploadTestData
    {
        public long size = 0;
        public int jobid = 0;

        public uploadTestData(int jobid, long size)
        {
            this.size = size;
            this.jobid = jobid;
        }
    }

    /// <summary>
    /// Interaction logic for OptionsWindow.xaml
    /// </summary>
    public partial class OptionsWindow : Window
    {
        private const int parallelMax = 20;

        private MainWindow mainWindow;
        private BackgroundWorker bw;
        private BackgroundWorker[] uploadTasks;

        private long[] chunkSizes = {
            (long)1024 * 100,
            (long)1024 * 500,
            (long)1024 * 1024,
            (long)1024 * 1024 * 10,
            (long)1024 * 1024 * 20,
            (long)1024 * 1024 * 50,
            (long)1024 * 1024 * 80,
            (long)1024 * 1024 * 100,
            (long)1024 * 1024 * 200,
            (long)1024 * 1024 * 500,
            (long)1024 * 1024 * 1024,
            (long)1024 * 1024 * 1536
        };
        private bool cb100k = false;
        private bool cb500k = false;
        private bool cb1m = false;  
        private bool cb10m = false;
        private bool cb20m = false;
        private bool cb50m = false;
        private bool cb80m = false;
        private bool cb100m = false;
        private bool cb200m = false;
        private bool cb500m = false;
        private bool cb1g = false;
        private bool cb15g = false;

        private static bool IsTextAllowed(string text)
        {
            Regex regex = new Regex("[^0-9.-]+"); //regex that matches disallowed text
            return !regex.IsMatch(text);
        }

        public OptionsWindow(MainWindow mainWindow)
        {
            this.mainWindow = mainWindow;

            InitializeComponent();

            ServicePointManager.DefaultConnectionLimit = parallelMax;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.CheckCertificateRevocationList = false;

            sliderChunkSize.Value = Array.IndexOf(chunkSizes, Properties.Settings.Default.chunkSize);
            sliderParallel.Value = Properties.Settings.Default.parallelUploads;
            sliderDataQueue.Value = Properties.Settings.Default.dataQueue;
            sliderMaxFilesPerChunk.Value = Properties.Settings.Default.maxFilesPerChunk;
            sliderMaxRetries.Value = Properties.Settings.Default.maxRetries;
            checksums.IsChecked = Properties.Settings.Default.checksum;

            proxy.SelectedIndex = Properties.Settings.Default.proxy;
            proxyHost.Text = Properties.Settings.Default.proxyHost;
            proxyPort.Text = Properties.Settings.Default.proxyPort;

            SliderChunkSize_ValueChanged(null, null);
            SliderParallel_ValueChanged(null, null);
            SliderDataQueue_ValueChanged(null, null);
            SliderMaxFilesPerChunk_ValueChanged(null, null);
            SliderMaxRetries_ValueChanged(null, null);
            proxy_SelectionChanged(null, null);
        }

        private double sendTestChunk(int i, long size)
        {
            byte[] data = new byte[size];

            try
            {
                DateTime st = DateTime.Now;

                string output = "";
                string options = "{\"filename\":\"mdpush.test/mdpush.test." + i + "\",\"offset\":0,\"chunksize\":" + size + ",\"size\":" + size + "}";
                string header = mainWindow.user + "\n" + mainWindow.pass + "\n[" + options + "]\n";

                WebRequest httpWebRequest = WebRequest.Create("https://" + Properties.Settings.Default.cloudstorURL + "/rocket/upload.php");
                httpWebRequest.ContentType = "application/json";
                httpWebRequest.Method = "POST";
                httpWebRequest.Timeout = int.MaxValue;
                if (Properties.Settings.Default.proxy == 1)
                {
                    httpWebRequest.Proxy = null;
                }
                else if (Properties.Settings.Default.proxy == 2)
                {
                    httpWebRequest.Proxy = new WebProxy(Properties.Settings.Default.proxyHost, Int32.Parse(Properties.Settings.Default.proxyPort));
                }
                using (var streamWriter = new BinaryWriter(httpWebRequest.GetRequestStream()))  //new StreamWriter(httpWebRequest.GetRequestStream()))
                {
                    streamWriter.Write(header.ToCharArray());
                    streamWriter.Write(data);
                    streamWriter.Flush();
                    streamWriter.Close();
                }
                //win7 GC.Collect();
                using (var streamReader = new StreamReader(httpWebRequest.GetResponse().GetResponseStream()))
                {
                    output = streamReader.ReadToEnd().TrimEnd();
                    streamReader.Close();
                }
                //win7 GC.Collect();

                return Math.Round(DateTime.Now.Subtract(st).TotalSeconds,2);
            }
            catch 
            {
                return -1;
            }
        }

        private void Bw_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            try
            {
                Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                {
                    this.Dispatcher.Invoke(() =>
                    {
                        log.Text += (string)e.UserState + "\n";
                        log.ScrollToEnd();
                    });
                }));
            }
            catch
            {
                //nothing
            }
        }

        private void UploadChunkWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker worker = sender as BackgroundWorker;
            try
            {
                uploadTestData info = e.Argument as uploadTestData;
                sendTestChunk(info.jobid, info.size);
            }
            catch
            { 
                worker.ReportProgress(0, "ERROR!");
            }
        }

        private void Bw_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker worker = sender as BackgroundWorker;

            double time = 0;
            List<long> sizes = new List<long>();
            List<int> parallels = new List<int>();

            sizes.Add(0);
            if (cb100k) sizes.Add(chunkSizes[0]);
            if (cb500k) sizes.Add(chunkSizes[1]);
            if (cb1m)   sizes.Add(chunkSizes[2]);
            if (cb10m)  sizes.Add(chunkSizes[3]);
            if (cb20m)  sizes.Add(chunkSizes[4]);
            if (cb50m)  sizes.Add(chunkSizes[5]);
            if (cb80m)  sizes.Add(chunkSizes[6]);
            if (cb100m) sizes.Add(chunkSizes[7]);
            if (cb200m) sizes.Add(chunkSizes[8]);
            if (cb500m) sizes.Add(chunkSizes[9]);
            if (cb1g)   sizes.Add(chunkSizes[10]);
            if (cb15g)  sizes.Add(chunkSizes[11]);

            parallels.Add(1);
            parallels.Add(2);
            parallels.Add(4);
            parallels.Add(6);
            parallels.Add(8);
            parallels.Add(10);
            parallels.Add(12);
            parallels.Add(24);

            worker.ReportProgress(0, "Starting tests");
            foreach (int parallel in parallels)
            { 
                foreach (long size in sizes)
                {
                    DateTime st = DateTime.Now;
                    for (int i = 0; i < parallel; i++)
                    {
                        uploadTasks[i].RunWorkerAsync(new uploadTestData(i + 1, size));
                    }

                    int tasksBusy = parallelMax;
                    while (tasksBusy > 0)
                    {
                        tasksBusy = parallelMax;
                        for (int i = 0; i < parallelMax; i++)
                        {
                            if (uploadTasks[i] == null || (uploadTasks[i] != null && !uploadTasks[i].IsBusy))
                            {
                                tasksBusy--;
                            }
                        }
                    }
                    time = Math.Round(DateTime.Now.Subtract(st).TotalSeconds, 2);

                    if (size > 0)
                    {
                        worker.ReportProgress(0, "  " + parallel + " x " + mainWindow.FormatBytes(size) + " in " + time + "s " + mainWindow.FormatBytes(Math.Round(size * parallel / time)) + "/s (" + mainWindow.FormatBytes(size * 8 * parallel / time) + "its/s)");
                    }
                    else
                    {
                        worker.ReportProgress(0, "  " + parallel + " x " + mainWindow.FormatBytes(size) + " in " + time + "s");
                    }
                    //win7 GC.Collect();
                }
                worker.ReportProgress(0, "");
            }

            worker.ReportProgress(0, "Tests Complete");
        }

        private void TestButton_Click(object sender, RoutedEventArgs e)
        {
            log.Text = "";

            cb100k = checkBox_100k.IsChecked.Value;
            cb500k = checkBox_500k.IsChecked.Value;
            cb1m   = checkBox_1m.IsChecked.Value;
            cb10m  = checkBox_10m.IsChecked.Value;
            cb20m  = checkBox_20m.IsChecked.Value;
            cb50m  = checkBox_50m.IsChecked.Value;
            cb80m  = checkBox_80m.IsChecked.Value;
            cb100m = checkBox_100m.IsChecked.Value;
            cb200m = checkBox_200m.IsChecked.Value;
            cb500m = checkBox_500m.IsChecked.Value;
            cb1g   = checkBox_1g.IsChecked.Value;
            cb15g  = checkBox_15g.IsChecked.Value;

            uploadTasks = new BackgroundWorker[parallelMax];
            for (int i = 0; i < parallelMax; i++)
            {
                uploadTasks[i] = new BackgroundWorker();
                uploadTasks[i].DoWork += new DoWorkEventHandler(UploadChunkWork);
                uploadTasks[i].ProgressChanged += new ProgressChangedEventHandler(Bw_ProgressChanged);
                //uploadTasks[i].RunWorkerCompleted += new RunWorkerCompletedEventHandler(bw_RunWorkerCompleted);
                uploadTasks[i].WorkerReportsProgress = true;
                uploadTasks[i].WorkerSupportsCancellation = true;
            }

            bw = new BackgroundWorker();
            bw.DoWork += new DoWorkEventHandler(Bw_DoWork);
            bw.ProgressChanged += new ProgressChangedEventHandler(Bw_ProgressChanged);
            //bw.RunWorkerCompleted += new RunWorkerCompletedEventHandler(bw_RunWorkerCompleted);
            bw.WorkerReportsProgress = true;
            bw.WorkerSupportsCancellation = true;

            bw.RunWorkerAsync();
        }

        private void SliderChunkSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            labelChunkSize.Content = mainWindow.FormatBytes(chunkSizes[Convert.ToInt32(sliderChunkSize.Value)]);
        }

        private void SliderParallel_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            labelParallel.Content = sliderParallel.Value;
        }

        private void SliderDataQueue_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            labelDataQueue.Content = sliderDataQueue.Value;
        }

        private void SliderMaxFilesPerChunk_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            labelMaxFilesPerChunk.Content = sliderMaxFilesPerChunk.Value;
        }

        private void SliderMaxRetries_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            labelMaxRetries.Content = sliderMaxRetries.Value;
        }

        private void proxyPort_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            e.Handled = !IsTextAllowed(e.Text);
        }

        private void proxyPort_PastingHandler(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(String)))
            {
                String text = (String)e.DataObject.GetData(typeof(String));
                if (!IsTextAllowed(text)) e.CancelCommand();
            }
            else e.CancelCommand();
        }

        private void proxy_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (proxyHost == null || proxyPort == null)
                return;

            if (proxy.SelectedIndex == 2)
            {
                proxyHost.IsEnabled = true;
                proxyPort.IsEnabled = true;
            }
            else
            {
                proxyHost.IsEnabled = false;
                proxyPort.IsEnabled = false;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.chunkSize = chunkSizes[Convert.ToInt32(sliderChunkSize.Value)];
            Properties.Settings.Default.parallelUploads = Convert.ToInt32(sliderParallel.Value);
            Properties.Settings.Default.dataQueue = Convert.ToInt32(sliderDataQueue.Value);
            Properties.Settings.Default.maxFilesPerChunk = Convert.ToInt32(sliderMaxFilesPerChunk.Value);
            Properties.Settings.Default.maxRetries = Convert.ToInt32(sliderMaxRetries.Value);
            Properties.Settings.Default.checksum = checksums.IsChecked.Value;
            Properties.Settings.Default.proxy = proxy.SelectedIndex;
            Properties.Settings.Default.proxyHost = proxyHost.Text;
            Properties.Settings.Default.proxyPort = proxyPort.Text;
            Properties.Settings.Default.Save();
            mainWindow.UpdateUploadLabel();

            TimeSpan t = TimeSpan.FromSeconds(0.2);
            DoubleAnimation a = new DoubleAnimation();
            a.From = 0.0;
            a.To = 1.0;
            a.Duration = t;
            DoubleAnimation b = new DoubleAnimation();
            b.From = 1.0;
            b.To = 0.0;
            b.Duration = t;
            b.BeginTime = t;

            Storyboard sb = new Storyboard();
            sb.Children.Add(a);
            sb.Children.Add(b);
            Storyboard.SetTarget(a, SettingsPanel);
            Storyboard.SetTarget(b, SettingsPanel);
            Storyboard.SetTargetProperty(a, new PropertyPath("Opacity"));
            Storyboard.SetTargetProperty(b, new PropertyPath("Opacity"));
            sb.Begin();
        }
    }
}
