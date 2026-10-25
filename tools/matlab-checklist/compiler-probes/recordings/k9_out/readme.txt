K9 MATLAB Archive (.NET Component)

1. Prerequisites for Deployment 

Verify that MATLAB Runtime(R2025b) is installed.   
If not, you can run the MATLAB Runtime installer.
To find its location, enter
  
    >>mcrinstaller
      
at the MATLAB prompt.
NOTE: You will need administrator rights to run the MATLAB Runtime installer. 

Alternatively, download and install the Windows version of the MATLAB Runtime for R2025b 
from the following link on the MathWorks website:

    https://www.mathworks.com/products/compiler/mcr/index.html
   
For more information about the MATLAB Runtime and the MATLAB Runtime installer, see 
"Distribute Applications" in the MATLAB Compiler SDK documentation  
in the MathWorks Documentation Center.

2. Files to Deploy and Package

-K9.ctf
   -contains the generated component to be used by MATLAB Compiler SDK's MATLAB Data Array based .NET APIs. 
-strongly_typed_interface folder
   -contains the generated C# files for strongly typed interface to be used by MATLAB Compiler SDK's MATLAB Data Array based .NET APIs. 
-This readme file

3. Definitions

For information on deployment terminology, go to
https://www.mathworks.com/help and select MATLAB Compiler >
Getting Started > About Application Deployment >
Deployment Product Terms in the MathWorks Documentation
Center.

* NOTE: <mcr_root> is the directory where the MATLAB Runtime is installed on the target 
        machine.
        <matlab_root> is the directory where MATLAB is installed on the target machine.

