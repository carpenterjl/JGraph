function k6_err(varargin)
%K6_ERR Stage K6's failing main: a line, then an uncaught error. Built console and windowed, to see
%where a windowed program's error report goes and what its exit code is.
k0_quiet;
fprintf('before\n');
log = getenv('K6_LOG');
if ~isempty(log)
    fid = fopen(log, 'a');
    fprintf(fid, '%s\tbefore\n', char(datetime('now', 'Format', 'HH:mm:ss.SSS')));
    fclose(fid);
end
error('k6:boom', 'k6 failed on purpose');
end
