% midi_devices.m -- mididevinfo, mididevice and midicontrols on this machine's MIDI devices (device
% classes plan, stage D10b, ADR 0194): the list (the MIDI Mapper and the GS Wavetable Synth, no
% inputs), mididevice's argument checks and its object, midisend's and midireceive's refusals, and
% midicontrols, midiread, midisync, midicallback and midiid with no MIDI input attached. Outputs are
% opened and closed; nothing is ever sent, so the synthesizer hears nothing. Recorded from R2025b on
% this machine: its rows name this machine's devices.

% --- the list
info = mididevinfo;
ix_chk('info', info);
ix_chk('in_size', size(info.input));
ix_chk('in_fields', fieldnames(info.input)');
ix_chk('out_size', size(info.output));
ix_chk('out_names', {info.output.Name});
ix_chk('out_if', {info.output.Interface});
ix_chk('out_ids', [info.output.ID]);
ix_chk('listing', ix_flat(evalc('mididevinfo')));
devs = midicontrols.devices;
ix_chk('devs', devs);
ix_chk('devs_size', size(devs));
ix_chk('devs_input', [devs.input]);
ix_chk('devs_names', {devs.name});

% --- mididevice's checks
ix_chk('c_none', dv_err(@() mididevice()));
ix_chk('c_three', dv_err(@() mididevice('Input', 0, 'Output')));
ix_chk('c_five', dv_err(@() mididevice(1, 2, 3, 4, 5)));
ix_chk('c_bad_name', dv_err(@() mididevice('no such device')));
ix_chk('c_bad_id', dv_err(@() mididevice(99)));
ix_chk('c_neg_id', dv_err(@() mididevice(-1)));
ix_chk('c_frac_id', dv_err(@() mididevice(0.5)));
ix_chk('c_vec_id', dv_err(@() mididevice([0 1])));
ix_chk('c_bad_io', dv_err(@() mididevice('Sideways', 0)));
ix_chk('c_in_out', dv_err(@() mididevice('Input', 0)));
ix_chk('c_cell', dv_err(@() mididevice({1})));
ix_chk('c_by_part', dv_err(@() mididevice('Micro')));
ix_chk('c_input_name', dv_err(@() mididevice('Input', 'Microsoft MIDI Mapper')));
ix_chk('c_output_not', dv_err(@() mididevice('Output', 'no such device')));
ix_chk('c_four_bad', dv_err(@() mididevice('Output', 0, 'Output', 1)));
ix_chk('c_four_in', dv_err(@() mididevice('Output', 0, 'Input', 1)));

% --- a mididevice on the MIDI Mapper
d = mididevice(0);
ix_chk('dev_disp', midi_disp(d));
ix_chk('dev_class', class(d));
ix_chk('dev_props', properties(d));
ix_chk('dev_methods', methods(d));
ix_chk('dev_values', sprintf('%s|%d|%d|%s', d.Output, d.OutputID, d.InputID, class(d.Input)));
ix_chk('dev_input_size', size(d.Input));   % the 1-by-0 char (open item 31, ADR 0219)
ix_chk('dev_isa', sprintf('%d %d %d', isa(d, 'handle'), isvalid(d), isobject(d)));
ix_chk('dev_by_name', midi_try(@() midi_output_id(mididevice('Microsoft GS Wavetable Synth'))));
ix_chk('dev_output_kw', midi_output_id(mididevice('Output', 0)));
ix_chk('dev_output_kw_name', midi_try(@() midi_output_id(mididevice('Output', 'GS Wavetable'))));
d2 = mididevice(0);
ix_chk('dev_twice', sprintf('%d %d', d2.OutputID, d.OutputID));
clear d2
ix_chk('dev_get', dv_err(@() get(d, 'OutputID')));
ix_chk('dev_set', dv_err(@() set(d, 'OutputID', 3)));
ix_chk('dev_dot_set', dv_err(@() midi_set(d, 'OutputID', 3)));
ix_chk('dev_no_prop', dv_err(@() d.Bogus));
ix_chk('receive_out', dv_err(@() midireceive(d)));
ix_chk('receive_method', dv_err(@() receive(d)));
ix_chk('hasdata_out', dv_err(@() hasdata(d)));
ix_chk('send_none', dv_err(@() midisend(d)));
ix_chk('send_bad_type', dv_err(@() midisend(d, 'Bogus')));
ix_chk('send_bad_chan', dv_err(@() midisend(d, 'NoteOn', 0, 60, 64)));
ix_chk('send_not_device', dv_err(@() midisend(5, 'Start')));
ix_chk('receive_not_device', dv_err(@() midireceive(5)));
ix_chk('receive_none', dv_err(@() midireceive()));
ix_chk('callback_not_controls', dv_err(@() midicallback(d)));
delete(d);
ix_chk('dev_deleted', isvalid(d));

% --- midicontrols with no MIDI input
lastwarn('');
mc = midicontrols(1081, 0.5);
[msg, id] = lastwarn;
ix_chk('mc_warning', [id ' ## ' msg]);
lastwarn('');
mcd = midicontrols(7, 'MIDIDevice', 'nothing here');
[msg, id] = lastwarn;
ix_chk('mc_device_warning', [id ' ## ' msg]);
ix_chk('mc_disp', midi_disp(mc));
ix_chk('mc_disp_any', midi_disp(midicontrols));
ix_chk('mc_disp_range', midi_disp(midicontrols([1 2 3 4])));
ix_chk('mc_disp_list', midi_disp(midicontrols([1 5 9])));
ix_chk('mc_disp_device', midi_disp(mcd));
ix_chk('mc_class', class(mc));
ix_chk('mc_props', properties(mc));
ix_chk('mc_methods', methods(mc));
ix_chk('mc_isa', isa(mc, 'handle'));
ix_chk('e_ctl_neg', dv_err(@() midicontrols(-1)));
ix_chk('e_ctl_frac', dv_err(@() midicontrols(1.5)));
ix_chk('e_ctl_128', dv_err(@() midicontrols(128)));
ix_chk('e_ctl_chan', dv_err(@() midicontrols(17001)));
ix_chk('e_ctl_char', dv_err(@() midicontrols('abc')));
ix_chk('e_init_big', dv_err(@() midicontrols(7, 2)));
ix_chk('e_init_raw_big', dv_err(@() midicontrols(7, 200, 'OutputMode', 'rawmidi')));
ix_chk('e_init_raw_frac', dv_err(@() midicontrols(7, 2.5, 'OutputMode', 'rawmidi')));
ix_chk('e_init_size', dv_err(@() midicontrols([1 2], [0.1 0.2 0.3])));
ix_chk('e_mode', dv_err(@() midicontrols(7, 'OutputMode', 'loud')));
ix_chk('e_dev', dv_err(@() midicontrols(7, 'MIDIDevice', 5)));
ix_chk('e_param', dv_err(@() midicontrols(7, 'Bogus', 5)));
ix_chk('e_many', dv_err(@() midicontrols(1, 2, 3)));
ix_chk('e_param_novalue', dv_err(@() midicontrols(7, 0.5, 'OutputMode')));
ix_chk('read_one', midiread(mc));
ix_chk('read_method', read(mc));
ix_chk('read_vec', midiread(midicontrols([1 2 3], [0 0.25 1])));
ix_chk('read_raw', midiread(midicontrols([1 2], [3 127], 'OutputMode', 'rawmidi')));
ix_chk('read_raw_prefix', midiread(midicontrols(1, 9, 'OutputMode', 'raw')));
ix_chk('read_mat', midiread(midicontrols([1 2; 3 4], 0.1)));
ix_chk('read_any', midiread(midicontrols));
ix_chk('read_scaled', midiread(midicontrols(1:11, 0:0.1:1)));
ix_chk('read_edges', [midiread(midicontrols(1, 0.001)), midiread(midicontrols(1, 0.3)), midiread(midicontrols(1, 1))]);
ix_chk('read_two', dv_err(@() midi_read_two(mc)));
ix_chk('read_not_controls', dv_err(@() midiread(5)));
ix_chk('sync_none', evalc('midisync(mc)'));
midisync(mc, 0.75);
ix_chk('sync_no_device', midiread(mc));
ix_chk('sync_bad', dv_err(@() midisync(mc, 2)));
ix_chk('sync_bad_size', dv_err(@() midisync(midicontrols([1 2 3], 0), [0.1 0.2])));
ix_chk('sync_many', dv_err(@() midisync(mc, 0.1, 0.2)));
ix_chk('cb_get', midicallback(mc));
f = @(h) disp(1);
ix_chk('cb_set_old', midicallback(mc, f));
ix_chk('cb_get2', isequal(midicallback(mc), f));
ix_chk('cb_clear_old', isequal(midicallback(mc, []), f));
ix_chk('cb_get3', midicallback(mc));
ix_chk('cb_bad', dv_err(@() midicallback(mc, 5)));
ix_chk('cb_many', dv_err(@() midicallback(mc, [], [])));
ix_chk('cb_noout', evalc('midicallback(mc, f)'));
[ctl, dev] = midiinfo(mc);
ix_chk('info_hidden', sprintf('%s %d %s %d %d', class(ctl), ctl, class(dev), size(dev)));
ix_chk('info_any', midiinfo(midicontrols));
lastwarn('');
[c, dn] = midiid;
[msg, id] = lastwarn;
ix_chk('midiid', sprintf('%s ## %s ## %d %d %s %d %d', id, msg, size(c), class(dn), size(dn)));
ix_chk('trace', midicontrols.trace);
ix_chk('trace_bad', dv_err(@() midicontrols.trace(1)));
