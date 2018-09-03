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

reload(sys)
sys.setdefaultencoding('utf8')

with open('/conf/config.json') as json_data:
        config = json.load(json_data)
       
	path = config["owncloudRoot"] + sys.argv[1]

	status, result = client.FileSystem(config["eosurl"]).query(QueryCode.CHECKSUM, arg=path)

	if (status.ok == False):
		json_log_print("query()")
		sys.exit(status)

	print result.replace("adler32 ","").upper()
