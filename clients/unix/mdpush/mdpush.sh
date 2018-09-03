#!/bin/bash

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
# THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS “AS IS”
# AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
# IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
# DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
# FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
# DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
# SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
# CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
# OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
# OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

rocketURL="https://server"

startT=$(date +%s)
totalSize=0

user="${1}"
from="${2}"
to="${3}"

echo -n "Enter Password:"
read -s pass
echo
echo

chunksizeMax=100000000
parallelMax=12

function ceildiv(){ echo $((($1+$2-1)/$2)); }

function secondstohr() {
	T=$1
	D=$((T/60/60/24))
	H=$((T/60/60%24))
	M=$((T/60%60))
	S=$((T%60))
	(( $D > 0 )) && printf '%d days ' $D
	(( $H > 0 )) && printf '%d hours ' $H
	(( $M > 0 )) && printf '%d minutes ' $M
	(( $D > 0 || $H > 0 || $M > 0 )) && printf 'and '
	printf '%d seconds\n' $S
}

function bytestohr() {
    SLIST="b,KB,MB,GB,TB,PB,EB,ZB,YB"

    POWER=1
    VAL=$( echo "scale=2; $1 / 1" | bc)
    VINT=$( echo $VAL / 1024 | bc )
    while [ $VINT -gt 0 ]
    do
        let POWER=POWER+1
        VAL=$( echo "scale=2; $VAL / 1024" | bc)
        VINT=$( echo $VAL / 1024 | bc )
    done

    echo $VAL$( echo $SLIST | cut -f$POWER -d, )
}

function waitForJobs() {
	while [ $(jobs -r | wc -l) -ge ${parallelMax} ]; do
		echo "Waiting for jobs to finish"
		sleep 1
	done
}

function newChunkFile(){
	f=$(mktemp /tmp/mdpush.chunk.XXXXXX)
	echo "${user}" > ${f}
	echo "${pass}" >> ${f}
	echo "${f}"
}

function parallelCurl() {
	chunkfile="${1}"
	curl -ks --request POST --header "Content-Type: application/json" --data-binary "@${chunkfile}" "${rocketURL}/upload.php"
	rm -f "${chunkfile}"
}

function uploadChunk(){
	chunkfile="${1}"
	chunkfileOptions="${2}"

	echo "[${chunkfileOptions:1}]" >> "${chunkfile}"
	cat "${chunkfile}.data" >> "${chunkfile}"
	rm -f "${chunkfile}.data"

	waitForJobs

	parallelCurl "${chunkfile}" &
}

function joinChunks(){
	waitForJobs
}

chunkfile=$(newChunkFile "${user}" "${pass}")
chunkfileOptions=""
chunksize=0
while read file; do
	size=$(stat -c %s "${file}")
	offset=0
	totalSize=$(expr ${totalSize} + ${size})

	tofile="${to}/${file:${#from}}"
	echo "PROCESSING ${file} : ${size}"

	while [ $(expr ${size} - ${offset}) -gt $(expr ${chunksizeMax} - ${chunksize}) ]; do
		chunkfileOptions=$(echo "${chunkfileOptions},{\"filename\":\"$(echo "${tofile}" | sed 's/\//\\\//g')\",\"offset\":${offset},\"chunksize\":$(expr ${chunksizeMax} - ${chunksize}),\"size\":${size}}")
		#dd ibs=1 obs=${chunksizeMax} skip=${offset} count=$(expr ${chunksizeMax} - ${chunksize}) if="${file}" >> "${chunkfile}.data" 2>/dev/null
		./mdpushReadFile.py "${file}" ${offset} $(expr ${chunksizeMax} - ${chunksize}) >> "${chunkfile}.data"
		offset=$(expr ${offset} + $(expr ${chunksizeMax} - ${chunksize}))

		uploadChunk "${chunkfile}" "${chunkfileOptions}" 
		chunkfile=$(newChunkFile "${user}" "${pass}")
		chunkfileOptions=""
		chunksize=0
	done
	if [ $(expr ${size} - ${offset}) -gt 0 ]; then
		difference=$(expr ${size} - ${offset})
		chunkfileOptions=$(echo "${chunkfileOptions},{\"filename\":\"$(echo "${tofile}" | sed 's/\//\\\//g')\",\"offset\":${offset},\"chunksize\":${difference},\"size\":${size}}")
		#dd ibs=1 obs=${chunksizeMax} skip=${offset} count=${difference} if="${file}" >> "${chunkfile}.data" 2>/dev/null
		./mdpushReadFile.py "${file}" ${offset} ${difference} >> "${chunkfile}.data"
		offset=$(expr ${offset} + ${difference})
		chunksize=$(expr ${chunksize} + ${difference})

		if [ ${chunksize} -ge ${chunksizeMax} ]; then
			uploadChunk "${chunkfile}" "${chunkfileOptions}" 
			chunkfile=$(newChunkFile "${user}" "${pass}")
			chunkfileOptions=""
			chunksize=0
		fi
	fi
done < <(find "${from}" -type f)
if [ ${chunksize} -gt 0 ]; then
	uploadChunk "${chunkfile}" "${chunkfileOptions}" 
fi

echo "waiting for final jobs to end"
wait

echo "Scanning new files..."
chunkfile=$(newChunkFile "${user}" "${pass}")
echo "{\"path\":\"${to}\"}" >> ${chunkfile}
curl -ks --request POST --header "Content-Type: application/json" --data-binary "@${chunkfile}" ${rocketURL}/scan.php
rm -f "${chunkfile}"

endT=$(date +%s)
Time=$(expr ${endT} - ${startT})
bps=$(expr ${totalSize} / ${Time})

echo "---------------------------------------------"
echo "Time taken:  $(secondstohr ${Time})"
echo "Data pushed: $(bytestohr ${totalSize})"
echo "Speed:       $(bytestohr ${bps})/s ($(bytestohr $(expr ${bps} \* 8))its/s)"
