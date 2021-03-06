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

list($user,$password,$options,$dataAll) = explode("\n",file_get_contents("php://input"),4);

owncloudWedavLogin($user,$password);

set_time_limit(0);

$chunks=json_decode($options,true);
unSet($options);

$output=array('chunks'=>array());

if (isSet($chunks['chunks'])) {
	$offset=0;
	foreach($chunks['chunks'] as $chunk) {
		$data=substr($dataAll,$offset,$chunk['chunksize']);
		$output['chunks'][] = writeChunk($user,$chunk['filename'],$chunk['offset'],$chunk['chunksize'],$chunk['size'],$data);
		$offset+=$chunk['chunksize'];
	}
	$output['totaluploadsize']=0;
	foreach($output['chunks'] as $uploaded) {
		$output['totaluploadsize']+=$uploaded['to']-$uploaded['from'];
	}
} else {
	$output['error']='API ERROR';
}

echo json_encode($output);

?>
