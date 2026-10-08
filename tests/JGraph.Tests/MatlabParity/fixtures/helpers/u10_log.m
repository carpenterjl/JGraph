function out = u10_log(msg)
% The U10 fixtures' log: u10_log(msg) appends, u10_log() answers it joined and empties it.
persistent LOG
if isempty(LOG), LOG = {}; end
if nargin == 0
    out = strjoin(LOG, ' / ');
    LOG = {};
else
    LOG{end+1} = msg;
end
end
