function device_peer()
% DEVICE_PEER  Put the scriptable device on the far end of COM20 (device classes plan, test strategy).
%   In JGraph, jgraph.internal.devicesim registers a simulated COM20 for the session, shadowing any
%   real port of that name, with the peer engine on its far end (and COM21 listed as the peer's).
%   In MATLAB, tools/devices/peer-sim runs the same engine on COM21 of the com0com pair
%   COM20<->COM21; this starts it once per MATLAB process and waits for its READY line. Talk to the
%   device with dp(s, command).
persistent peer
try
    jgraph.internal.devicesim('COM20');
    return
catch
end
if ~isempty(peer) && ~peer.HasExited
    return
end
% helpers -> fixtures -> MatlabParity -> JGraph.Tests -> tests -> the repository
repo = fileparts(fileparts(fileparts(fileparts(fileparts(fileparts(mfilename('fullpath')))))));
exe = fullfile(repo, 'tools', 'devices', 'peer-sim', 'bin', 'Release', 'net8.0', 'peer-sim.exe');
if ~isfile(exe)
    error('device_peer:noPeer', 'Build %s first (dotnet build -c Release).', exe);
end
psi = System.Diagnostics.ProcessStartInfo(exe, sprintf('COM21 --parent %d --idle 300', feature('getpid')));
psi.UseShellExecute = false;
psi.RedirectStandardOutput = true;
psi.CreateNoWindow = true;
peer = System.Diagnostics.Process.Start(psi);
ready = char(peer.StandardOutput.ReadLine());
if ~strcmp(ready, 'READY')
    error('device_peer:notReady', 'peer-sim did not start: %s', ready);
end
end
