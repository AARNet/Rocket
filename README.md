# Rocket

This repository contains the files for building Rocket for client and server.

# Introduction

Rocket is an upload only tool that bundles and uploads payloads of data into cloud storage (OwnCloud/other) using parallel threads. A payload can consist of:

* Bundles of small files
* Chunks of large file(s)
* Structured file path(s)

These payloads are then faithfully reconstructed on the backend.

Settings such as payload sizes, number of threads, maximum number of files per payload, payloads to buffer in memory is all user modifiable. This means users can fine tune settings to best utilise their local network and PCs.

By keeping payload sizes consistent and by using parallel threads, in the right conditions, we are able to upload data as fast as the PC can read off the local disk. Rocket uploads files into our ownCloud data space, so it is possible to upload into a shared space and have files arrive at a group of users as files upload.

# How it works

Rocket is split into two major parts, a frontend and a backend. The frontend is a Windows application that lets the user adjust settings and to start transfers.

Once a transfer starts, ie the user clicks Push, Rocket begins to scan the directory specified and creates payloads of a user-defined size. Once a payload is created, it is sent to Rocket’s backend via a https web call. Payloads are sent as soon as they are made up to a user-defined limit. As the number of concurrent uploads are limited/set by the user, it is possible to read of the disk faster than upload. Because of this Rocket can buffer payloads in to memory limited/set by the user.

The frontend creates payloads and sends it via https to the backend. The current supported backend is a combination of haproxy, apache, php-fpm, php7, xroot and CERN's EOS ([cern-eos](https://github.com/cern-eos)). When a payload reaches the backend, it is broken up into files or file chunks depending on its type and reassembled on EOS via the xroot API. Xroot is used because it allows us to interact with EOS directly thus bypassing ownCloud’s webdav gateway (used by the ownCloud sync client). This allows us to ignore EOS checksum checks on partial files and only generate a checksum once the file is uploaded, this increases file IO dramatically in EOS when uploading large files. Once a transfer has completed the Rocket frontend sends a signal to ownCloud to update the metadata cache for new and updated files, allowing users to see the files in their home directory or in a shared directory.

Other backends can be used that implements the Rocket API. See below for API details.

## License

```
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
```

## Server

Requires web server with php70 and python2-xrootd-4.8.3

Requires database server MySQL/MariaDB (used for collecting stats)

PHP code must be accessed from a url with `/rocket/`

Also requires access to owncloud's webdav (for authentication only)

Also requires access to owncloud's database for shares access

##### Note:
config.json is used by both the web php code as well as the python code.

It is possible to use cache such as redis, memcache or php sessions to cache data for more speed.

## Client(s)

### Windows
Requires Visual Studio 2017 .NET 4.6 x64

### Unix
Work in progress, early stages, may not follow current API

## Rocket API

##### HTTP POST to "https://server/rocket/upload.php"

Used to upload file chunks into EOS

POST body is a 4+ line text block using \n as line break

Line 1: username

Line 2: password (owncloud sync password)

Line 3: JSON object of chunks

Line 4+: RAW chunk data


Line 3 is a JSON describing how line 4 is to be chopped up.

for each file chunk:

&nbsp; &nbsp; filename :        Relative path of destination from users home directory (must use / as directory separator and must always start with a /)
        
&nbsp; &nbsp; offset :          Where in the file to start writing the data where 0 is the start of the file

&nbsp; &nbsp; chunksize :       Size of the chunk being uploaded

&nbsp; &nbsp; size :            Size of the file being uploaded (used to report back stats)

eg:
```json
[{"filename":"/test1.txt","offset":0,"chunksize":10,"size":10},{"filename":"/test2.txt","offset":0,"chunksize":5,"size":5}]
```

Line 4 for this example may look like:

`012345678901234`

FULL EXAMPLE:
```
test.user@somewhere.au
testpassword
[{"filename":"/test1.txt","offset":0,"chunksize":10,"size":10},{"filename":"/test2.txt","offset":0,"chunksize":5,"size":5}]"
012345678901234
```

In the above example:

username is "test.user@aarnet.edu.au"

password is "testpassword"

Two files:

&nbsp; &nbsp; /test1.txt

&nbsp; &nbsp; /test2.txt

Contents of /test1.txt:

`0123456789`

Contents of /test2.txt:

`01234`

##### HTTP POST to "https://server/rocket/scan.php"

Used to tell owncloud to file scan new file.

POST body is a 3 line text block using \n as line break


Line 1: username

Line 2: password (owncloud sync password)

Line 3: JSON object of path


Line 3 is a JSON which must have `path`

eg:
```json
{"path":"/test1.txt"}
```

##### HTTP POST to "https://server/rocket/login.php"

Used to test auth with owncloud.

This also checks that the user is using the current version of the Rocket Client


POST body is a 3 line text block using \n as line break


Line 1: username

Line 2: password (owncloud sync password)

Line 3: JSON object of path to where user wants to upload (can be a share)


Line 3 is a JSON which must have `path`


eg:
```json
{"path":"/folder"}
```

##### HTTP POST to "https://server/rocket/folders.php"

Used to list folders a user can upload to including shares.


POST body is a 3 line text block using \n as line break


Line 1: username

Line 2: password (owncloud sync password)

Line 3: JSON object which can contain `refresh` which when set to true will force Rocket to not use cached results.


eg:
```json
{"refresh":true}
```
