function results = build_hello_app(outputDir)
%BUILD_HELLO_APP Builds the Hello World app into a standalone Windows application.
%   results = build_hello_app(outputDir) builds HelloApp.m, with hello_wave.m found by dependency
%   analysis, into <outputDir>\HelloApp.exe (examples\hello-app\output by default). The program
%   runs on a machine with the JGraph Runtime of this version installed, or with
%   JGRAPH_RUNTIME_ROOT naming a runtime laid out by installer\publish-runtime.ps1. The same file
%   builds the same app with MATLAB Compiler under R2025b.
here = fileparts(mfilename('fullpath'));
if nargin < 1
    outputDir = fullfile(here, 'output');
end
results = compiler.build.standaloneWindowsApplication(fullfile(here, 'HelloApp.m'), ...
    'ExecutableName', 'HelloApp', 'OutputDir', outputDir);
end
