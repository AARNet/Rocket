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
using System.Net;
using System.Diagnostics;

namespace mdpushWorker
{
    class Program
    {
        private static void writeLog(string message, EventLogEntryType e)
        {
            try
            {
                EventLog eventLog = new EventLog();
                if (!EventLog.SourceExists("CloudStorRocket"))
                {
                    EventLog.CreateEventSource("CloudStorRocket", "Application");
                }
                eventLog.Source = "CloudStorRocket";
                eventLog.WriteEntry(message, e);
                eventLog.Close();
            }
            catch
            {
                //nothing
            }
            return;
        }

        static int Main(string[] args)
        {
            int maxRetry = 10;
            int pass = 0;
            bool complete = false;

            MemoryStream input = new MemoryStream();
            using (Stream stdin = Console.OpenStandardInput())
            {
                stdin.CopyTo(input);
            }

            while (pass<maxRetry && !complete)
            {
                pass++;
                try
                {
                    //ServicePointManager.DefaultConnectionLimit = 100;
                    //ServicePointManager.Expect100Continue = false;
                    //ServicePointManager.CheckCertificateRevocationList = false;

                    string mdpushURL = "https://" + mdpush.Properties.Settings.Default.cloudstorURL + "/rocket/";
                    WebRequest httpWebRequest = WebRequest.Create(mdpushURL + "upload.php");
                    httpWebRequest.ContentType = "application/json";
                    httpWebRequest.Method = "POST";
                    httpWebRequest.Timeout = 600000; //Timeout.Infinite; //60000;
                    if (mdpush.Properties.Settings.Default.proxy == 1)
                    {
                        httpWebRequest.Proxy = null;
                    }
                    else if (mdpush.Properties.Settings.Default.proxy == 2)
                    {
                        httpWebRequest.Proxy = new WebProxy(mdpush.Properties.Settings.Default.proxyHost, Int32.Parse(mdpush.Properties.Settings.Default.proxyPort));
                    }

                    using (var streamWriter = new BinaryWriter(httpWebRequest.GetRequestStream()))
                    {
                        byte[] buffer = new byte[2048];
                        int bytes;
                        input.Seek(0, SeekOrigin.Begin);
                        while ((bytes = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            streamWriter.Write(buffer);
                        }
                        streamWriter.Flush();
                        streamWriter.Close();
                    }
                    using (var streamReader = new StreamReader(httpWebRequest.GetResponse().GetResponseStream()))
                    {
                        Console.Write(streamReader.ReadToEnd().TrimEnd());
                        streamReader.Close();
                    }

                    complete = true;
                }
                catch (Exception e)
                {
                    string sEvent = "Rocket Worker\nException: " + e.Message;

                    if (e.Source != null)
                        sEvent += "\nSource: " + e.Source;

                    sEvent += "\n\nException\n" + e;
                    writeLog(sEvent, EventLogEntryType.Error);
                }
            }

            input.Flush();
            input.Close();

            if (pass >= maxRetry)
            {
                writeLog("Rocket Worker\nMax retries exceeded", EventLogEntryType.Error);
                return 1;
            }

            return 0;
        }
    }
}
