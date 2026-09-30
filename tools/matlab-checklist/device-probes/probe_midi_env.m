% PROBE_MIDI_ENV  MIDI devices and mididevice's checks in R2025b (device classes plan, stage D10b).
%   Nothing is sent: an output is opened and closed, which the synthesizer does not hear.
dv_px('list', 'mididevinfo')
dv_pr('info', 'mididevinfo')
info = mididevinfo;
dv_pr('in', 'info.input')
dv_pr('out', 'info.output')
for k = 1:numel(info.output)
    dv_pr(sprintf('out%d', k), sprintf('info.output(%d)', k))
    dv_pr(sprintf('out%d_name', k), sprintf('info.output(%d).Name', k))
    dv_pr(sprintf('out%d_if', k), sprintf('info.output(%d).Interface', k))
    dv_pr(sprintf('out%d_id', k), sprintf('info.output(%d).ID', k))
end
for k = 1:numel(info.input)
    dv_pr(sprintf('in%d_name', k), sprintf('info.input(%d).Name', k))
    dv_pr(sprintf('in%d_id', k), sprintf('info.input(%d).ID', k))
end
dv_pr('devs', 'midicontrols.devices')
dv_pr('c_none', 'mididevice()')
dv_pr('c_three', 'mididevice(''Input'', 0, ''Output'')')
dv_pr('c_five', 'mididevice(1,2,3,4,5)')
dv_pr('c_bad_name', 'mididevice(''no such device'')')
dv_pr('c_bad_id', 'mididevice(99)')
dv_pr('c_neg_id', 'mididevice(-1)')
dv_pr('c_frac_id', 'mididevice(0.5)')
dv_pr('c_bad_io', 'mididevice(''Sideways'', 0)')
dv_pr('c_in_out', 'mididevice(''Input'', 0)')
dv_pr('c_cell', 'mididevice({1})')
if ~isempty(info.output)
    o = info.output(1);
    dv_px('dev_out', sprintf('d = mididevice(%d); disp(d); disp(class(d)); disp(d.Output); disp(d.OutputID); disp(d.InputID); disp(class(d.Input)); disp(size(d.Input))', o.ID))
    d = mididevice(o.ID);
    dv_pr('dev_props', 'properties(d)')
    dv_pr('dev_methods', 'methods(d)')
    dv_pr('dev_by_name', sprintf('get(mididevice(''%s''), ''OutputID'')', o.Name))
    dv_pr('dev_by_part', sprintf('get(mididevice(''%s''), ''OutputID'')', o.Name(1:5)))
    dv_pr('dev_output_kw', sprintf('mididevice(''Output'', %d).OutputID', o.ID))
    dv_pr('dev_input_kw_on_out', sprintf('mididevice(''Input'', %d)', o.ID))
    dv_pr('dev_input_name_on_out', sprintf('mididevice(''Input'', ''%s'')', o.Name))
    dv_pr('receive_out', 'midireceive(d)')
    dv_pr('hasdata_out', 'hasdata(d)')
    dv_pr('send_none', 'midisend(d)')
    dv_pr('send_bad', 'midisend(d, 5)')
    dv_pr('set_out', 'set(d, ''OutputID'', 3)')
    dv_px('dot_set', 'd.OutputID = 3;')
    dv_pr('isa_handle', 'isa(d, ''handle'')')
    clear d
end
dv_pr('callback_bad', 'midicallback(1)')
