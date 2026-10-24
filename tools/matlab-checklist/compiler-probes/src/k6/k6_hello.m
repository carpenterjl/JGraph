function k6_hello(varargin)
%K6_HELLO Stage K6's console and windowed main (compiler and SDK plan): its arguments, whether it is
%deployed, one line on standard error, and its end. A windowed build's output may go nowhere, so
%with K6_LOG set each line is also appended to that file.
k0_quiet;
say('nargin\t%d', nargin);
for k = 1:nargin
    say('arg%d\t%s', k, varargin{k});
end
say('isdeployed\t%d', isdeployed);
fprintf(2, 'stderr\tline\n');
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
