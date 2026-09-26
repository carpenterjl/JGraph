function out = ix_evlog(varargin)
% IX_EVLOG  The event log of net_events_delegates. With arguments: append one entry, the tag and
%   ix_show of the rest. Without: return the log joined with " ; " (or "none") and clear it.
persistent LOG
if isempty(LOG)
    LOG = {};
end
if nargin > 0
    parts = cellfun(@ix_show, varargin(2:end), 'UniformOutput', false);
    LOG{end + 1} = strjoin([varargin(1) parts], ' ');
    if nargout > 0
        out = [];
    end
    return
end
if isempty(LOG)
    out = 'none';
else
    out = strjoin(LOG, ' ; ');
end
LOG = {};
end
