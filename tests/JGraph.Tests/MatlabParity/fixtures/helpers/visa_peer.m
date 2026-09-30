function visa_peer()
% VISA_PEER  The instruments behind the visadev fixtures (device classes plan, stage D4): the peer
%   engine on the far end of COM20 (device_peer), on a raw socket at 127.0.0.1:5025
%   (TCPIP0::127.0.0.1::5025::SOCKET) and behind a HiSLIP server at 127.0.0.1:4880
%   (TCPIP0::127.0.0.1::hislip0::INSTR). The two network peers answer *IDN? with
%   "JGraph,PeerSim,SN0001,1.0" even after a reset. Talk to any of them with dp(v, command).
%   In JGraph, jgraph.internal.visasim puts the same three behind a simulated VISA; in MATLAB, this
%   starts tools/devices/peer-sim twice more and waits for each READY line.
persistent peers
device_peer();
try
    jgraph.internal.visasim('on');
    return
catch
end
if ~isempty(peers) && ~peers{1}.HasExited && ~peers{2}.HasExited
    return
end
% helpers -> fixtures -> MatlabParity -> JGraph.Tests -> tests -> the repository
repo = fileparts(fileparts(fileparts(fileparts(fileparts(fileparts(mfilename('fullpath')))))));
exe = fullfile(repo, 'tools', 'devices', 'peer-sim', 'bin', 'Release', 'net8.0', 'peer-sim.exe');
if ~isfile(exe)
    error('visa_peer:noPeer', 'Build %s first (dotnet build -c Release).', exe);
end
idn = ['--on 2A49444E3F0A=' sprintf('%02X', double(sprintf('JGraph,PeerSim,SN0001,1.0\n')))];
peers = {start(exe, 'tcp:5025', idn), start(exe, 'hislip:4880', idn)};
end

function p = start(exe, where, idn)
psi = System.Diagnostics.ProcessStartInfo(exe, sprintf('%s --parent %d --idle 300 %s', where, feature('getpid'), idn));
psi.UseShellExecute = false;
psi.RedirectStandardOutput = true;
psi.CreateNoWindow = true;
p = System.Diagnostics.Process.Start(psi);
ready = char(p.StandardOutput.ReadLine());
if ~strcmp(ready, 'READY')
    error('visa_peer:notReady', 'peer-sim %s did not start: %s', where, ready);
end
end
