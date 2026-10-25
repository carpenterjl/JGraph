K9 MATLAB Archive (.NET Component)

1. Prerequisites for Deployment 

The JGraph Runtime 0.2.3 must be installed on the computer the archive runs on,
or its folder named by the JGRAPH_RUNTIME_ROOT environment variable. Clients run on .NET 8 or later.

2. Files to Deploy and Package

-K9.ctf
   -the deployable archive a client opens with MATLABRuntime.StartMATLAB. 
-strongly_typed_interface folder
   -the C# files of the strongly typed interface, which K9.dll is compiled from. 
-K9.dll
   -the strongly typed interface, for a client to reference. 
-JGraph.MATLAB.Runtime.dll
   -the MATLAB Data API, from the JGraph Runtime's extern\dotnet\net8.0 folder.
-This readme file

3. Definitions

The JGraph Runtime is the engine a built component runs on, installed once per JGraph version.
The MATLAB Data API keeps MathWorks' names under the JGraph.MATLAB namespaces: a client written for
MATLAB's runtime changes its using lines from MathWorks.MATLAB to JGraph.MATLAB.
