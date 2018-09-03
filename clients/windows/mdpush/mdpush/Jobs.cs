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
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Jobs
{
    public delegate void JobEventHandler(object source, JobEventArgs e);
    public class JobEventArgs : EventArgs
    {
        private int id;
        private string stdout = "";
        private string stderr = "";
        private int exitcode;

        public JobEventArgs(int id, int exitcode, string stdout, string stderr)
        {
            this.id = id;
            this.exitcode = exitcode;
            this.stdout = stdout;
            this.stderr = stderr;
        }

        public int getID()
        {
            return this.id;
        }

        public string getStdout()
        {
            return "" + this.stdout;
        }

        public string getStderr()
        {
            return "" + this.stderr;
        }

        public int getExitCode()
        {
            return this.exitcode;
        }
    }

    public delegate void JobsEventHandler(object source, JobsEventArgs e);
    public class JobsEventArgs : EventArgs
    {
        private JobEventArgs jobEventArgs;

        public JobsEventArgs(JobEventArgs jobEventArgs)
        {
            this.jobEventArgs = jobEventArgs;
        }

        public JobEventArgs getJobEventArgs()
        {
            return this.jobEventArgs;
        }
    }

    public class Job
    {
        public event JobEventHandler OnJobDone;

        private Process process;
        private bool busy = false;
        private int id = 0;
        private StringBuilder stdout = new StringBuilder();
        private StringBuilder stderr = new StringBuilder();

        public Job(int i)
        {
            id = i;
        }

        public bool IsBusy()
        {
            return this.busy;
        }

        public void SetBusy(bool b)
        {
            this.busy = b;
        }

        public int GetId()
        {
            return this.id;
        }

        public string getStdout()
        {
            if (stdout != null) { 
                return this.stdout.ToString();
            }
            return "";
        }

        public string getStderr()
        {
            if (stderr != null)
            {
                return this.stderr.ToString();
            }
            return "";
        }

        public DateTime GetExitTime()
        {
            if (this.process != null)
            {
                return this.process.ExitTime;
            }
            return DateTime.MinValue;
        }

        public int GetExitCode()
        {
            if (this.process != null)
            {
                return this.process.ExitCode;
            }
            return -1;
        }

        public void Kill()
        {
            try
            {
                this.process.Kill();
            }
            catch
            {
                //nothing to kill
            }
        }

        private void Process_Exited(object sender, EventArgs e)
        {
            this.stdout?.AppendLine(this.process.StandardOutput.ReadToEnd());
            this.stderr?.AppendLine(this.process.StandardError.ReadToEnd());

            int exitCode = this.process.ExitCode;
            this.process?.Close();

            OnJobDone?.Invoke(this, new JobEventArgs(this.id, exitCode, getStdout(), getStderr()));
            //this.busy = false;
        }

        /*private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!String.IsNullOrEmpty(e.Data))
            {
                this.stdout.AppendLine(e.Data);
            }
        }*/

        /*private void Process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!String.IsNullOrEmpty(e.Data))
            {
                this.stderr.AppendLine(e.Data);
            }
        }*/

        public StreamWriter RunJob(string dir, string cmd, string arg)
        {
            if (this.busy)
            {
                return null;
            }

            this.busy = true;
            this.stderr.Clear();
            this.stdout.Clear();

            this.process = null;
            this.process = new Process();
            this.process.StartInfo.CreateNoWindow = true;
            this.process.EnableRaisingEvents = true;
            this.process.StartInfo.UseShellExecute = false;
            this.process.StartInfo.RedirectStandardInput = true;
            this.process.StartInfo.RedirectStandardOutput = true;
            this.process.StartInfo.RedirectStandardError = true;
            this.process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
            this.process.StartInfo.StandardErrorEncoding = Encoding.UTF8;
            this.process.Exited += new EventHandler(Process_Exited);
            //this.process.OutputDataReceived += new DataReceivedEventHandler(Process_OutputDataReceived);
            //this.process.ErrorDataReceived += new DataReceivedEventHandler(Process_ErrorDataReceived);
            this.process.StartInfo.FileName = dir + cmd;
            this.process.StartInfo.WorkingDirectory = dir;
            this.process.StartInfo.Arguments = arg;
            this.process.Start();
            //this.process.BeginOutputReadLine();
            //this.process.BeginErrorReadLine();

            return this.process.StandardInput;
        }
    }

    public class Jobs
    {
        public event JobsEventHandler OnJobDone;
        private Job[] jobs;

        public Jobs(int size)
        {
            jobs = new Job[size];

            for (int i = 0; i < size; i++)
            {
                jobs[i] = new Job(i);
                jobs[i].OnJobDone += (sender, e) => Jobs_OnJobDone(sender, e, i);
            }
        }

        private void Jobs_OnJobDone(object source, JobEventArgs e, int id)
        {
            OnJobDone?.Invoke(this, new JobsEventArgs(e));
        }

        ~Jobs()
        {
            foreach (Job j in jobs)
            {
                j.Kill();
            }
        }

        public Job[] allJobs()
        {
            return jobs;
        }

        public int jobsBusy()
        {
            int i = 0;
            foreach (Job j in jobs)
            {
                if (j != null && j.IsBusy())
                    i++;
            }
            return i;
        }

        public int jobsNotBusy()
        {
            return jobs.Length - jobsBusy();
        }
    }
}
