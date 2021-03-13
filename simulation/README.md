### DynStack Simulations

This repository contains the simulations that are used in the 2021 GECCO competition on dynamic stacking problems. The implementations are made with the Sim# simulation framework and can be run on any major operating system for which .NET 5.0 is available (Windows, Mac OS, various Linux distributions).

To run experiments with a starter kit on a _locally running_ simulation perform the following steps:

1. Launch the simulation runner `dotnet run -p DynStack.SimulationRunner --sim HS --url tcp://127.0.0.1:2222 --id 658f9b28-6686-40d2-8800-611bd8466215 --settings Default`
2. Connect one of the solvers of the starter kits (in a separate console and in the folder of e.g. the csharp starter kit): `dotnet run tcp://127.0.0.1:2222 658f9b28-6686-40d2-8800-611bd8466215 HS`

Note that the id, url, and the type of simulation (HS or RM) need to be the same in both calls.

#### Simulation Settings

The simulation cannot start without defining some of its parameters first. The `Settings` class in the `DynStack.DataModel` library defines the specific settings for each simulation.

Using the default settings allows you to quickly get started, but represents just one single scenario from a wide range of possible configurations. When the `--settings` option is used either to point to a ProtoBuf file that contains the settings or with the `Default` string, then the simulation will run right away in real-time mode. When that option is not passed, then the solver has to send a SimControl message with the settings to be used. Thus, you have multiple options to control how the simulation loads the settings.

In general, parameterizing a simulation to yield a challenging scenario is not a trivial task. Remember that the arrival rate should always be lower than the processing rate to avoid creating a scenario that runs at 100% utilization. A way to calculate settings for the HS simulation has been published in Sebastian Raggl, Andreas Beham, Stefan Wagner, and Michael Affenzeller. 2020. Solution approaches for the dynamic stacking problem. In Proceedings of the 2020 Genetic and Evolutionary Computation Conference Companion (GECCO '20). Association for Computing Machinery, New York, NY, USA, 1652–1660. DOI:https://doi.org/10.1145/3377929.3398111.

#### Motivation and Outlook

We created these simulations to present challenges when solvers and optimization algorithms are deployed to control real-world systems. In typical static benchmark situations, e.g., a static block relocation problem, challenges that arise from uncertainties and dynamic events are not apparent and will only become visible when solved in an actual dynamic environment. With this project we hope to provide a tool for researchers to better understand and eventually overcome such challenges with new methods.