Templates for a C# based solvers.
================================

Prerequisits:
-------------
* [dotnet core](https://dotnet.microsoft.com/download)

Building:
---------

* install Google.Protobuf and NetMQ nuget Packages with:
> dotnet restore
* compile .proto file with:
> protoc.exe .\data_model.proto --csharp_out=csharp
* build with:
> dotnet build

Running:
--------
Find socket address, and simulation GUID on the competition website.

Run the rule based solver with for the hotstorage problem: 
> dotnet run tcp://1.2.3.4:8080 fbc6b6ab-9786-4068-986d-b0f5da49fa85 HS

Run the model based solver with for hotstorage problem: 
> dotnet run tcp://1.2.3.4:8080  fbc6b6ab-9786-4068-986d-b0f5da49fa85 HS --modelbased

Run the starterkit for the rollingmill problem
> dotnet run tcp://1.2.3.4:8080 fbc6b6ab-9786-4068-986d-b0f5da49fa85 RM