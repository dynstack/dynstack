

Templates for C++ based solvers.
================================

Prerequisits:
-------------
* [vcpkg](https://github.com/microsoft/vcpkg)
* [cmake](https://cmake.org/)
* [C++ compiler](https://visualstudio.microsoft.com/de/vs/)
* [protoc](https://developers.google.com/protocol-buffers/docs/downloads) must be in the PATH

Building:
---------
Install the dependencies:

> vcpkg install protobuf
> vcpkg install zeromq
> vcpkg install cppzmq

Set the environment variable VCPKG_ROOT to the directory where you installed vcpkg

Build the project:
> cd starterkits/cpp
> cmake -S . -B build
> cmake --build build


If you need to regenerate the model classes you can use.
> protoc .\hotstorage_model.proto --cpp_out=cpp/src/hotstorage
> protoc .\rollingmill_model.proto --cpp_out=cpp/src/rollingmill


Running:
--------
Find socket address, and simulation GUID on the competition website.

Run the hotstorage solver with for example: 
> .\build\Debug\stacking.exe tcp://1.2.3.4:8080 fbc6b6ab-9786-4068-986d-b0f5da49fa85 HS
 
Run the rollingmill solver with for example: 
> .\build\Debug\stacking.exe tcp://1.2.3.4:8080 fbc6b6ab-9786-4068-986d-b0f5da49fa85 RL
