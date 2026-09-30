function out = audio_log(varargin)
% AUDIO_LOG  The callback log of the audioplayer fixture. audio_log('clear') empties it; audio_log()
%   answers its entries joined by spaces, TimerFcn's left out (how many fire depends on timing);
%   audio_log(tag, obj, event) is the callback, and adds "tag:Running:event's class".
persistent entries
if isempty(entries)
    entries = {};
end
if nargin == 0
    kept = entries(~startsWith(entries, 'timer'));
    out = strjoin(kept, ' ');
    return
end
if nargin == 1
    entries = {};
    return
end
if ~ischar(varargin{1})
    % A cell callback {@audio_log, tag} is called as fcn(obj, event, tag).
    varargin = varargin([3 1 2]);
end
entries{end+1} = sprintf('%s:%s:%s', varargin{1}, varargin{2}.Running, class(varargin{3}));
end
