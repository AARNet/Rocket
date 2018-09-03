<?php
/*
BSD 3-Clause License

Copyright (c) 2018, AARNet
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
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/

include('lib.php');

list($user,$password,$options,$junk) = explode("\n",file_get_contents("php://input"),4);

owncloudWedavLogin($user,$password);

set_time_limit(0);

$files=json_decode($options,true);
unSet($options);

if (isSet($files['files'])) {
	foreach($files['files'] as $file) {
		$output=filecomplete($user,$file['filename'],$file['size'],chr($file['firstbyte']),$file['checksum']);
		if ($output['success']) {
			echo 'COMPLETE '.$file['filename']."\n";
			if ($file['checksum']!=-1 && ltrim($output['checksum'],'0')!=ltrim($file['checksum'],'0')) {
				echo 'WARNING  Checksums do not match, '.$file['checksum'].' vs '.$output['checksum'].' for file '.$file['filename']."\n";
			}
		} else {
			echo 'ERROR    for file '.$file['filename']."\n";
			print_r($output);
		}
	}
} else {
	echo "API ERROR\n";
}

?>
