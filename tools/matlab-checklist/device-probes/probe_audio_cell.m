function probe_audio_cell(o, e, tag)
% PROBE_AUDIO_CELL  A cell-array callback for probe_audio_player: {@probe_audio_cell, tag} is called as
%   fcn(obj, event, tag); it logs the tag and the arguments' classes.
global LOG
LOG{end+1} = sprintf('%s:%s:%s:%d', tag, class(o), class(e), nargin);
end
