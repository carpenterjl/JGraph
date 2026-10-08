function out = u10log(msg)
% Appends to the U10 probe's log; u10log() returns and clears it.
persistent LOG
if isempty(LOG), LOG = {}; end
if nargin == 0
    out = strjoin(LOG, ' / ');
    LOG = {};
else
    LOG{end+1} = msg;
end
end
