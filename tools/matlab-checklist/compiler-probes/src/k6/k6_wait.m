function k6_wait(varargin)
%K6_WAIT Stage K6's windowed main with no window: it logs its start, pauses 3 s and logs its end, so
%the splash's life can be held against the main's (K6_LOG names the log file).
k0_quiet;
say('start');
pause(3);
say('end');
end

function say(line)
fprintf('%s\n', line);
log = getenv('K6_LOG');
if ~isempty(log)
    fid = fopen(log, 'a');
    fprintf(fid, '%s\t%s\n', char(datetime('now', 'Format', 'HH:mm:ss.SSS')), line);
    fclose(fid);
end
end
