#!/usr/bin/python -u
# encoding=utf8

# BSD 3-Clause License
#
# Copyright (c) 2018, AARNet
# All rights reserved.
#
# Redistribution and use in source and binary forms, with or without
# modification, are permitted provided that the following conditions are met:
#
# * Redistributions of source code must retain the above copyright notice, this
#   list of conditions and the following disclaimer.
#
# * Redistributions in binary form must reproduce the above copyright notice,
#   this list of conditions and the following disclaimer in the documentation
#   and/or other materials provided with the distribution.
#
# * Neither the name of the copyright holder nor the names of its
#   contributors may be used to endorse or promote products derived from
#   this software without specific prior written permission.
#
# THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
# AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
# IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
# DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
# FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
# DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
# SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
# CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
# OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
# OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

import sys
import json
import time
import syslog
from XRootD import client
from XRootD.client.flags import *
from XRootD.client.responses import *

def json_log(s):
	a = {}
	a['app'] = 'Rocket'
	a['level'] = 'error'
	a['messsage'] = s
	syslog.openlog(str(config["syslogIdent"]), facility=syslog.LOG_USER)
	syslog.syslog(syslog.LOG_ERR, json.dumps(a))

def json_log_print(s):
	json_log(s)
	print "ERROR: " + s

#cat /etc/httpd/conf/httpd.conf | sudo -u apache ./writeChunk.py Michael.DSilva@aarnet.edu.au/files/testXrootd/httpd.conf 0 1

reload(sys)
sys.setdefaultencoding('utf8')

with open('/conf/config.json') as json_data:
        config = json.load(json_data)

	path = config["owncloudRoot"] + sys.argv[1]
	filename = 'root://' + config["eosurl"] + '/' + path
	offset = int(sys.argv[2])
	size = int(sys.argv[3])
	checksum = int(sys.argv[4])

	if (size == 0):
		checksum = 1

	extra = "?eos.ruid=48&eos.rgid=48"
	if (checksum == 0):
		extra = extra + "&eos.checksum=ignore"

	bookingsize = "&eos.bookingsize=" + str(size)

	status, null = client.FileSystem(config["eosurl"]).mkdir('/'.join(path.split('/')[:-1]) + extra, flags=MkDirFlags.MAKEPATH, mode=AccessMode.UR|AccessMode.UW|AccessMode.UX|AccessMode.GR|AccessMode.GX)
	if (status.ok == False):
		json_log_print("mkdir()")
        	sys.exit(status)

        with client.File() as f:
		status,null = f.open(filename + extra + bookingsize,flags=OpenFlags.NEW)

		opencount = 0
		while status.ok == False:
			f = client.File()
			status,null = f.open(filename + extra,flags=OpenFlags.UPDATE)
			if (status.ok == False):
				time.sleep(0.1)
				f = client.File()
				status,null = f.open(filename + extra,flags=OpenFlags.UPDATE)
				if (status.ok == False):
					if (opencount<=100):
						opencount += 1
						time.sleep(0.1)
						f = client.File()
						status,null = f.open(filename + extra + bookingsize,flags=OpenFlags.NEW)
					else:
						json_log_print("open()")
						sys.exit(status)
		if (opencount > 0):
			json_log_print("WARNING: Failed to open file on CloudStor " + str(opencount) + " times")
		
		if (offset >= 0 and size>0):
			status,null = f.write(sys.stdin.read(),offset=offset)
			if (status.ok == False):
				json_log_print("write()")
				sys.exit(status)

			if (checksum != 0):
				status,null = f.truncate(size)
				if (status.ok == False):
					json_log_print("truncate()")
					sys.exit(status)

#	status,null = f.close()
#	if (status.ok == False):
#		json_log_print("close()")
#		sys.exit(status)
