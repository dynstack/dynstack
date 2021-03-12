Templates for rust based solvers.
================================

Prerequisits:
-------------
* [rust](https://rustup.rs/)
* [protoc](https://developers.google.com/protocol-buffers/docs/downloads) must be in the rust directory.

Building:
---------

Zeromq is automatically compiled during the build
Protobuf is automatically compiled during the build.

> cargo build

Running:
--------
Find socket address, and simulation GUID on the competition website.

Run the rule based solver with for the hotstorage problem: 
> cargo run tcp://1.2.3.4:8080 fbc6b6ab-9786-4068-986d-b0f5da49fa85 HS

Run the model based solver with for hotstorage problem: 
> cargo run tcp://1.2.3.4:8080  fbc6b6ab-9786-4068-986d-b0f5da49fa85 HS --modelbased

Run the starterkit for the rollingmill problem
> cargo run tcp://1.2.3.4:8080 fbc6b6ab-9786-4068-986d-b0f5da49fa85 RM