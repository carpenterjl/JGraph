function audio_env()
% AUDIO_ENV  The audio devices behind the audioplayer and audiorecorder fixtures (device classes plan,
%   stage D10). In JGraph, jgraph.internal.audiosim puts two simulated inputs (IDs 0 and 1) and two
%   simulated outputs (IDs 2 and 3) in place of the machine's, so nothing is heard and no microphone
%   opens; in MATLAB this does nothing, and the fixtures play only zeros and never record. The IDs the
%   fixtures name are the same kind in both: 0 an input, 3 an output, 99 neither.
try
    jgraph.internal.audiosim('on');
catch
end
end
