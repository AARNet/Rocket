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

owncloudWedavLogin($user,$password,false);

$options=json_decode($options,true);

$refresh=isSet($options['refresh']) && $options['refresh'];

$user=getUserHome($user);
$shares=getSharedList($user,!$refresh);

$db = new mysqli($config['ownclouddbhost'], $config['ownclouddbuser'], $config['ownclouddbpassword'], $config['ownclouddbname'], $config['ownclouddbport']);

$sqls=array();
$sqls[]='SELECT SUBSTRING(filecache.path,6) as dirs from filecache LEFT JOIN storages ON filecache.storage=storages.numeric_id where storages.id="home::'.$user.'" AND filecache.mimetype=4 AND LEFT(filecache.path,6)="files/"';
foreach($shares as $share=>$data) {
	$sqls[]='SELECT CONCAT("'.$share.'",SUBSTRING(filecache.path,'.(strlen($data['path'])+2).')) as dirs from filecache LEFT JOIN storages ON filecache.storage=storages.numeric_id where storages.id="home::'.$data['user'].'" AND filecache.mimetype=4 AND (LEFT(filecache.path,'.(strlen($data['path'])+1).')="'.$data['path'].'/" OR filecache.path="'.$data['path'].'")';
}
$sql = implode(' UNION ',$sqls).' ORDER BY LOWER(dirs)';
$result = $db->query($sql);
if($result){
	while($row = $result->fetch_array(MYSQLI_NUM)) {
		if ($row[0]!='/Shared')
			echo $row[0]."\n";
	}
	$result->close();
}

$db->close();
?>
