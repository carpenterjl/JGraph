function p = ip_assets()
% IP_ASSETS  The staged interop assets (tools/interop/stage-assets.ps1), from the probe folder.
here = fileparts(mfilename('fullpath'));
repo = fileparts(fileparts(fileparts(here)));
p.root = fullfile(repo, 'tests', 'JGraph.Tests', 'MatlabParity', 'fixtures', 'interop');
p.assembly = fullfile(p.root, 'JGraph.Interop.TestAssembly.dll');
p.lib = fullfile(p.root, 'jgtestlib.dll');
p.header = fullfile(p.root, 'jgtestlib.h');
p.winheader = fullfile(p.root, 'jgtestlib_win.h');
p.native = fullfile(repo, 'tests', 'Interop', 'native');
p.here = here;
end
