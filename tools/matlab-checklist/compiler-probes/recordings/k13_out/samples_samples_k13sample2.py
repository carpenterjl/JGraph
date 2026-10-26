#!/usr/bin/env python
"""
Sample script that uses the k13s package created using
MATLAB Compiler SDK.

Refer to the MATLAB Compiler SDK documentation for more information.
"""

import k13s
# Import the matlab module only after you have imported 
# MATLAB Compiler SDK generated Python modules.
import matlab

try:
    my_k13s = k13s.initialize()
except Exception as e:
    print('Error initializing k13s package\n:{}'.format(e))
    exit(1)

try:
    xIn = matlab.double([1, 2, 3], size=(1, 3))
    aOut, bOut, cOut = my_k13s.k9multi(xIn, nargout=3)
    print(aOut, bOut, cOut, sep='\n')
except Exception as e:
    print('Error occurred during program execution\n:{}'.format(e))

my_k13s.terminate()