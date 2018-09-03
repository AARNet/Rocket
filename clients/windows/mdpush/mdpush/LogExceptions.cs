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
using System.IO;
using System.Windows;

namespace mdpush
{
    public partial class App : Application
    {
        private string lastMessage = "";

        private void LogException(Exception ex, string thread)
        {
            try
            {
                string logFile = mdpush.Properties.Settings.Default.logFile;
                if (logFile != "")
                {
                    using (StreamWriter w = File.AppendText(logFile))
                    {
                        string dt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ff") + " ";
                        w.WriteLine((dt + "================================================\nEXCEPTION:\n" + ex.Message + " (" + thread + " Thread)\n" + ex.StackTrace + "\n================================================").TrimEnd().Replace("\n", "\n" + dt));
                    }
                }
            }
            catch
            {
                //fail without noise
            }
        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);
        }

        void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e.ExceptionObject as Exception;
            if (ex.Message == lastMessage)
                return;

            LogException(ex, "Worker");

            MessageBox.Show("An unhandled exception just occurred:\n  " + ex.Message, "Please report this error and your log file to us", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            Exception ex = e.Exception;

            LogException(ex, "UI");

            e.Handled = false;
            MessageBoxResult dialogResult = MessageBox.Show("An unhandled exception just occurred:\n  " + ex.Message + "\n\nDo you want to continue?", "Please report this error and your log file to us", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (dialogResult == MessageBoxResult.Yes)
            {
                e.Handled = true;
                lastMessage = ex.Message;
            }
        }
    }
}
