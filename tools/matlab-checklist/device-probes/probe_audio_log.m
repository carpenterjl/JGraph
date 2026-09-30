function probe_audio_log(tag, o, e)
% PROBE_AUDIO_LOG  A callback for probe_audio_player: logs its tag, the object's class and Running,
%   and what the event argument is.
global LOG
if nargin < 3
    what = '<no event>';
elseif isempty(e)
    what = sprintf('%s empty', class(e));
elseif isstruct(e)
    what = sprintf('struct(%s)', strjoin(fieldnames(e)', ','));
elseif isobject(e)
    what = sprintf('%s(%s)', class(e), strjoin(properties(e)', ','));
else
    what = class(e);
end
LOG{end+1} = sprintf('%s:%s:%s:%s', tag, class(o), o.Running, what);
end
