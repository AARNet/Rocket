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

$config=json_decode(file_get_contents('/conf/config.json'),true);
$config['host'] = 'CloudStor';
$config['gap']  = '  ';//str_repeat(' ',strlen($config['host']));

function json_log($a) {
	global $config;
	$a['app'] = 'Rocket';
	$level = LOG_INFO;
	if ($a['level'] == 'error') $level=LOG_ERR;

	openlog($config['syslogIdent'], LOG_ODELAY, LOG_USER);
	syslog($level,json_encode($a));	
	//error_log(json_encode($a));
}

function json_log_die($a) {
	json_log($a);
	die(strtoupper($a['level']).': '.$a['message']);
}

function getCache($u,$key,$usecache=true) {
	global $config;
	if ($key=='') return false;
	$k='mdpush_'.$key;

	if (!$usecache) {
		unSetCache($u,$key);
		return false;
	} else if ($config['cache']=='memcache') {
		$m = new Memcached();
		$m->addServer($config['cacheHost'],$config['cachePort']);
		return $m->get($k);
	} else if ($config['cache']=='redis') {
		$r = new Redis();
		$r->connect($config['cacheHost'],$config['cachePort']);
		$r->auth($config['cachePass']);
		$out = $r->get($k);
		return $out===false ? false : unserialize($out);
	} else if ($config['cache']=='redisCluster') {
		$r = new RedisCluster(NULL, $config['cacheHosts']);
		$r->setOption(RedisCluster::OPT_SLAVE_FAILOVER, RedisCluster::FAILOVER_ERROR);
		$out = $r->get($k);
		return $out===false ? false : unserialize($out);
	} else if ($config['cache']=='session') {
		if (session_status() !== PHP_SESSION_ACTIVE) {
			session_id('MDPUSH_'.$u);
			session_start();
		}
		if (!isSet($_SESSION[$k])) return false;
		if ($_SESSION[$k]['expire']<time()) {
			unSet($_SESSION[$k]);
			return false;
		}
		return $_SESSION[$k]['value'];
	} else {
		return false;
	}
}

function setCache($u,$key, $v) {
	global $config;
	if ($key=='') return;
	$k='mdpush_'.$key;

	if ($config['cache']=='memcache') {
		$m = new Memcached();
		$m->addServer($config['cacheHost'],$config['cachePort']);
		$m->set($k, $v, $config['cacheLife']);
	} else if ($config['cache']=='redis') {
		$r = new Redis();
		$r->connect($config['cacheHost'],$config['cachePort']);
		$r->auth($config['cachePass']);
		$r->setex($k, $config['cacheLife'], serialize($v));
	} else if ($config['cache']=='redisCluster') {
		$r = new RedisCluster(NULL, $config['cacheHosts']);
		$r->setOption(RedisCluster::OPT_SLAVE_FAILOVER, RedisCluster::FAILOVER_ERROR);
		$r->setex($k, $config['cacheLife'], serialize($v));
	} else if ($config['cache']=='session') {
		if (session_status() !== PHP_SESSION_ACTIVE) {
			session_id('MDPUSH_'.$u);
			session_start();
		}
		$_SESSION[$k]=array('value'=>$v, 'expire'=>time()+$config['cacheLife']);
	}
}

function unSetCache($u,$key) {
	global $config;
	if ($key=='') return;
	$k='mdpush_'.$key;

	if ($config['cache']=='memcache') {
		$m = new Memcached();
		$m->addServer($config['cacheHost'],$config['cachePort']);
		$m->delete($k);
	} else if ($config['cache']=='redis') {
		$r = new Redis();
		$r->connect($config['cacheHost'],$config['cachePort']);
		$r->auth($config['cachePass']);
		$r->del($k);
	} else if ($config['cache']=='redisCluster') {
		$r = new RedisCluster(NULL, $config['cacheHosts']);
		$r->setOption(RedisCluster::OPT_SLAVE_FAILOVER, RedisCluster::FAILOVER_ERROR);
		$r->del($k);
	} else if ($config['cache']=='session') {
		if (session_status() !== PHP_SESSION_ACTIVE) {
			session_id('MDPUSH_'.$u);
			session_start();
		}
		if (isSet($_SESSION[$k])) unSet($_SESSION[$k]);
	}
}

function checkVersion($scan) {
	global $config;

	if (isSet($scan['version'])) {
		$c = $scan['version'];
		$s = $config['clientversion'];

		if ($c != $s) {
		        $old=false;
		        $client = explode('.',substr($c,1));
			if (count($client) == 3) {
		                $server = explode('.',substr($s,1));
				// v1.2.3 = 1 0002 0003 = 100020003 = 100,020,003
		                $clientNum = ($client[0]*100000000) + ($client[1]*10000) + $client[2];
		                $serverNum = ($server[0]*100000000) + ($server[1]*10000) + $server[2];

		                $old = $serverNum > $clientNum;
		        } else {
		                $old = true;
		        }
	        	if ($old) {
			        echo "\n";
			        echo "----------------------------------------------------------------------------------------------\n";
			        echo 'WARNING: The current version of rocket is '.$config['clientversion'].' please upgrade !'."\n";
			        echo '         '.$config['clientdownloadurl']."\n";
		        	echo "----------------------------------------------------------------------------------------------\n";
			        echo "\n";
		        }
		}
	}
}

function owncloudWedavLogin($user,$pass,$usecache=true) {
	global $config;

	$userLower = strtolower($user);

	if (getCache($userLower,'user_'.$userLower,$usecache) === false) {
		//curl -k -X PROPFIND -u user@somewhere.au:test https://owncloudserver/remote.php/webdav/
		$ch = curl_init();
		curl_setopt($ch, CURLOPT_URL, $config['owncloudWebdav']);
		curl_setopt($ch, CURLOPT_USERPWD, $user.':'.$pass);
		curl_setopt($ch, CURLOPT_CUSTOMREQUEST, 'PROPFIND');
		curl_setopt($ch, CURLOPT_RETURNTRANSFER, 1);
		$response = curl_exec($ch);
		curl_close($ch);

		if (strpos($response, '200 OK') === false) {
			//echo $response;
			//return false;
			json_log_die(array('level'=>'error','user'=>$user,'message'=>'Login Failed'));
		}
		setCache($userLower,'user_'.$userLower, time());
	}

	return true;
}

function storeStats($user,$stats) {
	global $config;

	$error='';
	$userLower = strtolower($user);
	$sql='INSERT INTO Stats(`Email`,`IP`,`CompleteTime`,`Time`,`Size`,`CompletedFiles`,`FailedFiles`,`ChunkSize`,`Parallel`,`Buffer`) VALUES("'.$userLower.'","'.strtolower($_SERVER['X_FORWARDED_FOR']).'",NOW(),'.$stats['time'].','.$stats['size'].','.$stats['completedFiles'].','.$stats['failedFiles'].','.$stats['chunkSize'].','.$stats['parallel'].','.$stats['buffer'].')';
	
	$db = new mysqli($config['ownclouddbhost'], $config['ownclouddbuser'], $config['ownclouddbpassword'], 'rocket', $config['ownclouddbport']);
	if (!$db->query($sql)) {
        	$error='ERROR: '.$db->error."\n";
        }
	$db->close();

	return $error;
}

function getSharedList($user,$usecache=true) {
	global $config;

	$userLower = strtolower($user);

	$list = getCache($userLower,'shares_'.$user,$usecache);
	if ($list === false) {
		$list=array();
		$sql='SELECT SUBSTRING(storages.id,7), filecache.path, share.file_target FROM share LEFT JOIN filecache ON share.file_source=filecache.fileid LEFT JOIN storages ON filecache.storage=storages.numeric_id WHERE (LOWER(share.share_with)="'.$userLower.'" OR share.share_with IN (SELECT gid FROM group_user WHERE LOWER(uid)="'.$userLower.'")) AND share.permissions & 3=3 AND storages.id IS NOT NULL AND filecache.path IS NOT NULL AND share.file_target IS NOT NULL AND LEFT(storages.id,6)="home::"';
		$db = new mysqli($config['ownclouddbhost'], $config['ownclouddbuser'], $config['ownclouddbpassword'], $config['ownclouddbname'], $config['ownclouddbport']);
		$result = $db->query($sql);
		if($result){
			while($row = $result->fetch_array(MYSQLI_NUM)) {
				$list[$row[2].'/'] = array(
						'fullpath' => $row[0].'/'.$row[1].'/',
						'user' => $row[0],
						'path' => $row[1]
					);
			}
			$result->close();
		}
		$db->close();
		setCache($userLower,'shares_'.$user, $list);
	}
	return $list;
}

function getUserHome($user,$usecache=true) {
	global $config;

	$userLower = strtolower($user);

	$userHome = getCache($userLower,'home_'.$userLower,$usecache);
	if ($userHome === false) {
		$sql='SELECT SUBSTRING(id,7) FROM storages WHERE LOWER(id) = "home::'.$userLower.'" LIMIT 1';
		$db = new mysqli($config['ownclouddbhost'], $config['ownclouddbuser'], $config['ownclouddbpassword'], $config['ownclouddbname'],$config['ownclouddbport']);
		$result = $db->query($sql);
		if($result){
			$row = $result->fetch_array(MYSQLI_NUM);
			$userHome = $row[0];
		} else {
			$userHome = $user;
		}
		setCache($userLower,'home_'.$userLower, $userHome);
	}
	return $userHome;
}

function bytesToString($s,$precision=2) {
        if ($s>1208925819614629174706176) { $s=$s/1208925819614629174706176; $ss=' YB'; } else
           if ($s>1180591620717411303424) { $s=$s/1180591620717411303424; $ss=' ZB'; } else
              if ($s>1152921504606846976) { $s=$s/1152921504606846976; $ss=' EB'; } else
                 if ($s>1125899906842624) { $s=$s/1125899906842624; $ss=' PB'; } else
                    if ($s>1099511627776) { $s=$s/1099511627776; $ss=' TB'; } else
                       if ($s>1073741824) { $s=$s/1073741824; $ss=' GB'; } else
                          if ($s>1048576) { $s=$s/1048576; $ss=' MB'; } else
                             if ($s>1024) { $s=$s/1024; $ss=' KB'; } else $ss=' Bytes';
	return round($s,$precision).$ss;
}


function getPath($user,$filename) {
	global $config;

	//dont want realative paths!
	if (strpos($filename,'/../') !== false ||
	    strpos($filename,'/~/') !== false) {
		json_log_die(array('level'=>'error','user'=>$user,'message'=>'Relative paths are not supported'));
	}

	//Shared are special
	if (substr($filename,0,8)=='/Shared/') {
		$shares = getSharedList($user);
		foreach($shares as $share => $data) {
			if (substr($filename,0,strlen($share))==$share) {
				return $data['fullpath'].substr($filename,strlen($share));
			}
		}
		json_log_die(array('level'=>'error','user'=>$user,'message'=>'Share Not Found, Upload Failed'));
        }

	//normal method
        return getUserHome($user).'/files/'.$filename;
}

function ocFilescan($user,$path) {
	global $config;

	$output='';
	$user = getUserHome($user);
	$u = $user;

	$DEBUG=array(
		'user' => $user,
		'pathInput' => $path
	);

	if (substr($path,0,8)=='/Shared/') {
	        $shares = getSharedList($user);
		$DEBUG['shares']=$shares;
	        foreach($shares as $share => $data) {
	                if (substr($path.'/',0,strlen($share))==$share) {
	                        $u = $data['user'];
	                        $path = substr($data['path'],5).'/'.substr($path,strlen($share));
	                        break;
	                }
	        }
	}

	$url = $config['owncloud'].'ocs/v1.php/cloud/users/'.$u.'/filescan?path='.urlencode($path).'&format=json';
	$DEBUG['u'] = $u;
	$DEBUG['url'] = $url;
	$DEBUG['path'] = $path;

	$scan=0;
	$scanmax=5;
	while($scan<$scanmax) {
		$scan++;
	
		$ch = curl_init();
		curl_setopt($ch, CURLOPT_URL, $url);
		curl_setopt($ch, CURLOPT_RETURNTRANSFER, 1);
		curl_setopt($ch, CURLOPT_USERPWD, $config['owncloudapiuser'].':'.$config['owncloudapipass']);
		$json = curl_exec($ch);
		curl_close($ch);

		$json_a=json_decode($json,true);
		
		if ($json_a['ocs']['meta']['statuscode']==100) {
			foreach($json_a['ocs']['data']['output'] as $line) {
				$out='';
                		if ($user==$u) {
		                        $out=str_replace('/'.$u.'/files/','',$line);
                		} else {
		                        $out=str_replace('/'.$u.'/files/','Shared/',$line);
                		}
		                $output.=$config['gap'].$out."\n";
			}
			echo $output;
			return;
		}
	}
	echo "ERROR: File scan failed.\n";
	foreach ($DEBUG as $k=>$v) json_log(array('level'=>'error','user'=>$user,'message'=>$k.': '.str_replace(PHP_EOL, '', print_r($v,true))));
}

function addFileToMessageBus($user,$path) {
	global $config;

	$user = getUserHome($user);
	$u = $user;
	$newpath=$path;

	if (substr($path,0,8)=='/Shared/') {
	        $shares = getSharedList($user);
	        foreach($shares as $share => $data) {
	                if (substr($path.'/',0,strlen($share))==$share) {
	                        $u = $data['user'];
	                        $newpath = substr($data['path'],5).'/'.substr($path,strlen($share));
	                        break;
	                }
	        }
	}
	$input = array(
		'file' => $config['owncloudRoot'].$u.'/files'.$newpath,
		'user' => $user,
		'logicalPath' => $path
	);

	$db = new mysqli($config['ownclouddbhost'], $config['ownclouddbuser'], $config['ownclouddbpassword'], 'messagebus', $config['ownclouddbport']);
	$sql='INSERT INTO Jobs(`JobType`, `JobInput`) VALUES("rocket.Scan","'.mysqli_real_escape_string($db,json_encode($input)).'")';
        if (!$db->query($sql)) {
                json_log(array('level'=>'error','user'=>$user,'message'=>$db->error));
        }
        $db->close();
}

function writeChunk($user,$filename,$offset,$chunksize,$size,$data,$checksum=false) {
	global $config;

	$output=array();
	$path=getPath($user,$filename);
	$dataSize=strlen($data);

	$descriptorspec = array(
	   0 => array("pipe", "r"),
	   1 => array("pipe", "w"),
	   2 => array("pipe", "w")
	);
	$process = proc_open(
	        '/scripts/writeChunk.py "'.$path.'" '.$offset.' '.$size.' '.($checksum?'1':'0'),
	        $descriptorspec,
	        $pipes
	);

	fwrite($pipes[0], $data);
	fclose($pipes[0]);

	$stdout = trim(stream_get_contents($pipes[1]));
	$stderr = trim(stream_get_contents($pipes[2]));

	fclose($pipes[1]);
	fclose($pipes[2]);
	$exit_status = proc_close($process);

	if ($exit_status!=0) {
		$output['output']=$config['gap'].str_replace(array("\r\n","\n"),"\n".$config['gap'],$stdout)."\n";
		$output['error']=$config['gap'].str_replace(array("\r\n","\n"),"\n".$config['gap'],$stderr)."\n";
		$output['from']=0;
		$output['to']=0;
		$output['size']=0;
	} else {
		$output['output']=str_replace(array("\r\n","\n"),"\n",$stdout);

		$output['from']=$offset;
		$output['to']=$dataSize+$offset;
		$output['size']=$size;

		//if ($offset+$chunksize >= $size) { //last chunk (in terms of filesize)
		//	addFileToMessageBus($user,$filename);
		//}
	}

	$output['filename']=$filename;
	$output['success']=$exit_status==0;

	return $output;
}

function filecomplete($user,$filename,$size,$firstbyte,$checksum) {
	global $config;

	$output=writeChunk($user,$filename,0,1,$size,$firstbyte,true);
	if ($output['success']) {
		//send to messagebus for filescan
		addFileToMessageBus($user,$filename);

		//get checksum
		if ($checksum!=-1) {
			$path=getPath($user,$filename);

			$descriptorspec = array(
			   0 => array("pipe", "r"),
				   1 => array("pipe", "w"),
			   2 => array("pipe", "w")
			);
			$process = proc_open(
			        '/scripts/getChecksum.py "'.$path.'"',
			        $descriptorspec,
			        $pipes
			);
			fclose($pipes[0]);

			$stdout = trim(stream_get_contents($pipes[1]));
			$stderr = trim(stream_get_contents($pipes[2]));

			fclose($pipes[1]);
			fclose($pipes[2]);
			$exit_status = proc_close($process);

			$output['error']=$config['gap'].str_replace(array("\r\n","\n"),"\n".$config['gap'],$stdout)."\n"
			                .$config['gap'].str_replace(array("\r\n","\n"),"\n".$config['gap'],$stderr)."\n";
			$output['checksum']=$stdout;
		}
	}
	return $output;
}
?>
