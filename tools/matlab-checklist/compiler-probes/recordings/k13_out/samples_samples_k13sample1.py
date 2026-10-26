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
    inIn = matlab.double([1.5], size=(1, 1))
    outOut = my_k13s.k9echo(inIn)
    print(outOut, sep='\n')
except Exception as e:
    print('Error occurred during program execution\n:{}'.format(e))

my_k13s.terminate()