function out = u11_log(msg)
% The U11 fixtures' log: u11_log(msg) appends, u11_log() answers it joined and empties it.
persistent LOG
if isempty(LOG), LOG = {}; end
if nargin == 0
    out = strjoin(LOG, ' / ');
    LOG = {};
else
    LOG{end+1} = msg;
end
end
