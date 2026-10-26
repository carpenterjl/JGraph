function probe_k14_server(port, cors, discovery, readyFile)
%PROBE_K14_SERVER Starts R2025b's in-MATLAB test server (the one productionServerArchiveCompiler's
%test client uses: serverSetup and serverStart from mps_in_matlab) over stage K14's functions
%(src\k14), as the HTTP oracle for the compiler and SDK plan's stage K14. Run with
%matlab -r, not -batch: the server answers only while MATLAB idles at its prompt. The file named
%by readyFile gets this MATLAB's process id once the server listens; record-k14.ps1 then runs
%src\k14client\k14client.py against it and ends the process.
beep('off');
here = fileparts(mfilename('fullpath'));
src = fullfile(here, 'src', 'k14');
addpath(src);
exported = {'k14add', 'k14echo', 'k14multi', 'k14err', 'k14noout', 'k14print', 'k14slow', 'k14nargs', ...
    'k14state', 'k14quiet', 'k14describe', 'k14make'}; % k14struct is left out: a function the component lacks
if discovery
    signatures = fullfile(src, 'k14sig.json');
else
    signatures = '';
end
serverSetup(port, logical(cors), logical(discovery), exported, signatures, 'k14comp');
serverStart();
fid = fopen(readyFile, 'w');
fprintf(fid, '%d\n', feature('getpid'));
fclose(fid);
fprintf('k14 server on port %d (cors %d, discovery %d)\n', port, cors, discovery);
end
