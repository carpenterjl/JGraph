function acc_log(line)
%ACC_LOG Appends a timed line to the file K6_LOG names (k6-windows.ps1's variable, whose watcher
%reads the clock column), for a windowed program whose standard output goes nowhere (stage K22).
log = getenv('K6_LOG');
if isempty(log)
    fprintf('%s\n', line);
    return
end
fid = fopen(log, 'a');
fprintf(fid, '%s\t%s\n', char(datetime('now', 'Format', 'HH:mm:ss.SSS')), line);
fclose(fid);
end
