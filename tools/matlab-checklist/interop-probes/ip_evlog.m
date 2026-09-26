function out = ip_evlog(varargin)
% IP_EVLOG  With arguments: append one described event to the log. Without: return the log
%   as one line and clear it.
global LOG
if nargin > 0
    parts = cellfun(@ip_describe, varargin(2:end), 'UniformOutput', false);
    LOG{end + 1} = strjoin([varargin(1) parts], ' ; ');
    if nargout > 0, out = []; end
    return
end
if isempty(LOG)
    out = '(none)';
else
    out = strjoin(LOG, ' || ');
end
LOG = {};
end
