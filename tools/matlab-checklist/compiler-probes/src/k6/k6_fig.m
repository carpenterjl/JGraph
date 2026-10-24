function k6_fig(varargin)
%K6_FIG Stage K6's windowed main: one visible figure, then the main returns. R2025b's runtime then
%waits for the figure to close; record-k6.ps1 closes it through UI Automation. With K6_LOG set
%each line is also appended to that file, with the time.
k0_quiet;
say('start');
f = figure('Name', 'k6 figure', 'NumberTitle', 'off');
plot(1:3, [2 4 3]);
drawnow;
say('shown %d', double(f.Number));
say('end');
end

function say(varargin)
line = sprintf(varargin{:});
fprintf('%s\n', line);
log = getenv('K6_LOG');
if ~isempty(log)
    fid = fopen(log, 'a');
    fprintf(fid, '%s\t%s\n', char(datetime('now', 'Format', 'HH:mm:ss.SSS')), line);
    fclose(fid);
end
end
