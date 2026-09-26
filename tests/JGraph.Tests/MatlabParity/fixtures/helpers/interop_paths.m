function p = interop_paths()
% INTEROP_PATHS  Where the interop test assets are, for the net_* and shrlib_* fixtures.
%   A fixture runs with the fixtures folder current in both engines, so the assets sit in
%   fixtures/interop/. tools/interop/stage-assets.ps1 fills that folder in the source tree before
%   MATLAB records; the test project copies the same files next to the fixtures in its output.
%   The folder is not committed: its contents are built (the assembly), committed elsewhere
%   (tests/Interop/native), or written by MATLAB (the thunk library).
root = fullfile(pwd, 'interop');
p.root = root;
p.assembly = fullfile(root, 'JGraph.Interop.TestAssembly.dll');
p.lib = fullfile(root, 'jgtestlib.dll');
p.header = fullfile(root, 'jgtestlib.h');
p.winheader = fullfile(root, 'jgtestlib_win.h');
p.proto = 'jgtestlib_proto';
end
