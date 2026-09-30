% PROBE_MIDI_SCALE  midicontrols' private scaling between raw MIDI values and [0, 1] in R2025b
%   (device classes plan, stage D10b), read by calling the private functions from their folder.
here = pwd;
addpath(here);
cd(fullfile(matlabroot, 'toolbox', 'audio', 'audio', 'private'));
dv_pr('from_raw', 'arrayfun(@midiScaleFromRaw, 0:127)')
dv_pr('to_raw', 'arrayfun(@midiScaleToRaw, 0:0.01:1)')
dv_pr('to_raw_edges', 'arrayfun(@midiScaleToRaw, [0.5/126, 1.5/126, 62.5/126, 63.5/126, 125.5/126, 0.999])')
dv_pr('count', 'midiCountDevices')
dv_pr('default_in', 'midiGetDefaultInputDevice')
dv_pr('info0', 'midiGetDeviceInfo(0)')
dv_pr('err_text', 'midiGetErrorMessage(-9999)')
cd(here);
