### DynStack Simulations

This repository contains the simulations that are used in the GECCO competitions on dynamic stacking problems. The implementations are made with the Sim# simulation framework and can be run on any major operating system for which .NET 5.0 is available (Windows, Mac OS, various Linux distributions).

As of May 2022 the simulation environments support two slightly different modes which will be explained in the following:
1. asynchronous (default)
2. synchronous (new!)

_Asynchronous_ mode is the default mode. In this mode, the simulation sends world updates to the address specified by the `--url` parameter and accepts messages from the policy. In this mode, the simulation always runs in _pseudo-realtime_ and does not await or block to receive the policy's decisions. This is close to a real-world scenario.

_Synchronous_ mode is an alternative mode in which the simulation and policy are synchronized. For this mode, the `--syncurl` parameter needs to be given and the policy has to react to every world update sent with an action message. The simulation is configured to block while waiting for the policy's answer and thus no simulation time passes. Because of this, the simulation runs in _virtual time_, i.e., as fast as possible. There is an option `--simulateasync` to delay the policy's answer and thus achieve similar behavior in the environment-policy interaction as in asynchronous mode. This mode is especially useful when a policy function is learnt using evolutionary methods, e.g., genetic programming or when parameters of a hand-written policy are tuned via evolutionary algorithms. Depending on the speed of the policy, a simulation run of 1 hour can be completed in a couple of seconds. Synchronous mode is currently implemented only for the HS environment.

To run experiments with a starter kit on a _locally running_ simulation in asynchronous mode perform the following steps:

1. Launch the simulation runner `dotnet run --project DynStack.SimulationRunner --sim HS --url tcp://127.0.0.1:2222 --id 658f9b28-6686-40d2-8800-611bd8466215 --settings Default`
2. Connect one of the solvers of the starter kits (in a separate console and *in the folder of e.g. the csharp starter kit*): `dotnet run tcp://127.0.0.1:2222 658f9b28-6686-40d2-8800-611bd8466215 HS`

In case you would want to use synchronous mode, then the command changes slightly:
1. `dotnet run --project DynStack.SimulationRunner --sim HS --url tcp://127.0.0.1:8080 --id 658f9b28-6686-40d2-8800-611bd8466215 --settings Default --syncurl tcp://127.0.0.1:2222`
2. The command for the solver/policy does not change

Note that the id, url, and the type of simulation (HS or RM) need to be the same in both calls.

#### Simulation Settings

The simulation cannot start without defining some of its parameters first. The `Settings` class in the `DynStack.DataModel` library defines the specific settings for each simulation.

Using the default settings allows you to quickly get started, but represents just one single scenario from a wide range of possible configurations. When the `--settings` option is used either to point to a ProtoBuf file that contains the settings or with the `Default` string, then the simulation will run right away in real-time mode. When that option is not passed, then the solver has to send a SimControl message with the settings to be used. Thus, you have multiple options to control how the simulation loads the settings.

In the folder `settings` you may also find the settings that are used for training runs during the competitions. You may thus also use one of these files when launching the simulation. For instance, to run the simulation with the "A-Easy" setting of the GECCO 2022 competition, you can provide the path as follows `dotnet run --project DynStack.SimulationRunner --sim HS --url tcp://127.0.0.1:2222 --id 658f9b28-6686-40d2-8800-611bd8466215 --settings settings/HS/GECCO2022/HS-Training-2022-A-Easy.buf` 

In general, parameterizing a simulation to yield a challenging scenario is not a trivial task. Remember that the arrival rate should always be lower than the processing rate to avoid creating a scenario that runs at 100% utilization. A way to calculate settings for the HS simulation has been published in Sebastian Raggl, Andreas Beham, Stefan Wagner, and Michael Affenzeller. 2020. Solution approaches for the dynamic stacking problem. In Proceedings of the 2020 Genetic and Evolutionary Computation Conference Companion (GECCO '20). Association for Computing Machinery, New York, NY, USA, 1652–1660. DOI:https://doi.org/10.1145/3377929.3398111.

#### Motivation and Outlook

We created these simulations to present challenges when solvers and optimization algorithms are deployed to control real-world systems. In typical static benchmark situations, e.g., a static block relocation problem, challenges that arise from uncertainties and dynamic events are not apparent and will only become visible when solved in an actual dynamic environment. With this project we hope to provide a tool for researchers to better understand and eventually overcome such challenges with new methods.