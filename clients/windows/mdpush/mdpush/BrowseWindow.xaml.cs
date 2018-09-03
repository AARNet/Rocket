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
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace mdpush
{
    /// <summary>
    /// Interaction logic for BrowseWindow.xaml
    /// </summary>
    public partial class BrowseWindow : Window
    {
        private MainWindow mainWindow;
        private BackgroundWorker bw;
        private bool error = true;

        public BrowseWindow(MainWindow mainWindow)
        {
            this.mainWindow = mainWindow;
            InitializeComponent();

            bw = new BackgroundWorker();
            bw.DoWork += new DoWorkEventHandler(bw_DoWork);
            bw.ProgressChanged += new ProgressChangedEventHandler(bw_ProgressChanged);
            //bw.RunWorkerCompleted += new RunWorkerCompletedEventHandler(bw_RunWorkerCompleted);
            bw.WorkerReportsProgress = true;
            bw.WorkerSupportsCancellation = true;
        }

        public void Reload(bool refresh)
        {
            error = true;
            list.Items.Clear();
            if (mainWindow.user != "" && mainWindow.pass != "")
            {
                list.Items.Add("Loading Folder List, please wait...");
                bw.RunWorkerAsync(refresh);
            }
            else
            {
                list.Items.Add("ERROR: Enter Username and Password");
            }
        }

        private void bw_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker worker = sender as BackgroundWorker;
            bool refresh = (bool)e.Argument;

            WebRequest httpWebRequest = WebRequest.Create("https://" + Properties.Settings.Default.cloudstorURL + "/rocket/folders.php");
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
            using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream()))
            {
                streamWriter.Write(mainWindow.user + "\n" + mainWindow.pass + "\n{\"refresh\":"+(refresh?"true":"false")+"}\n");
                streamWriter.Flush();
                streamWriter.Close();
            }
            using (var streamReader = new StreamReader(httpWebRequest.GetResponse().GetResponseStream()))
            {
                worker.ReportProgress(100, streamReader.ReadToEnd().TrimEnd());
                streamReader.Close();
            }
        }

        private void bw_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            try
            {
                if (Application.Current != null) Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                {
                    this.Dispatcher.Invoke(() =>
                    {
                        string[] dirs = ((string)e.UserState).Split('\n');
                        error = dirs[0].Substring(0, 1) != "/";
                        list.Items.Clear();
                        foreach (string dir in dirs)
                            list.Items.Add(dir);
                    });
                }));
            }
            catch
            {
                //nothing
            }
        }

        private void list_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!error && list.SelectedIndex >= 0)
                mainWindow.ToDir = list.SelectedItem.ToString();
        }

        private void list_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!error && list.SelectedIndex >= 0)
                Hide();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            Reload(true);
        }
    }
}
