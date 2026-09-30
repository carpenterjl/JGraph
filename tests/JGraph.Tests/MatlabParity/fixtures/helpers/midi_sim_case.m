function out = midi_sim_case(kind)
% MIDI_SIM_CASE  The cases of midi_sim (device classes plan, stage D10b, ADR 0194): mididevice,
%   midisend, midireceive and the midicontrols family on jgraph.internal.midisim's devices, whose
%   "JGraph Loopback" output delivers what it is sent to the "JGraph Loopback" input when it is due,
%   and whose "JGraph Sink" output keeps it. The answers are written from the rule: what mididevice.m,
%   midimsg.m and midicontrols.m make of those messages. Answers text.
jgraph.internal.midisim('on');
try
    switch kind
        case 'list'
            info = mididevinfo;
            out = sprintf('%s %d; ', info.input.Name, info.input.ID, info.output(1).Name, info.output(1).ID, info.output(2).Name, info.output(2).ID);
        case 'both'
            d = mididevice('JGraph Loopback');
            out = sprintf('%s %d %s %d', d.Input, d.InputID, d.Output, d.OutputID);
        case 'disp'
            out = ix_flat(evalc('disp(mididevice(''JGraph Loopback''))'));
        case 'roundtrip'
            d = mididevice('JGraph Loopback');
            before = hasdata(d);
            midisend(d, 'NoteOn', 3, 60, 64);
            pause(0.2);
            during = hasdata(d);
            m = midireceive(d);
            out = sprintf('%d %d %d | %s %d %d | %s %s %d %d %d | %d', before, during, hasdata(d), class(m), size(m), ...
                char(m.Type), mat2str(m.MsgBytes), m.Channel, m.Note, m.Velocity, m.Timestamp > 0);
        case 'nothing'
            d = mididevice('JGraph Loopback');
            m = midireceive(d);
            out = sprintf('%s %d %d', class(m), size(m));
        case 'most'
            d = mididevice('JGraph Loopback');
            midisend(d, [midimsg('Start'); midimsg('Continue'); midimsg('Stop')]);
            pause(0.2);
            first = midireceive(d, 2);
            rest = midireceive(d, Inf);
            none = midireceive(d, 0);
            out = sprintf('%d %d %d | %s %s %s', numel(first), numel(rest), numel(none), char(first(1).Type), char(first(2).Type), char(rest.Type));
        case 'most_bad'
            d = mididevice('JGraph Loopback');
            out = [dv_err(@() midireceive(d, -1)) ' // ' dv_err(@() midireceive(d, 1.5)) ' // ' dv_err(@() midireceive(d, [1 2])) ...
                ' // ' dv_err(@() midireceive(d, 'a')) ' // ' dv_err(@() midireceive(d, 1, 2))];
        case 'order'
            % Sent out of order, the messages go out in timestamp order, each after its delay.
            d = mididevice('JGraph Loopback');
            midisend(d, [midimsg('NoteOn', 1, 62, 64, 0.3); midimsg('NoteOn', 1, 60, 64, 0); midimsg('NoteOn', 1, 61, 64, 0.15)]);
            pause(0.6);
            m = midireceive(d);
            gaps = diff([m.Timestamp]);
            out = sprintf('%d %d %d | %d', m.Note, all(gaps > 0.08 & gaps < 0.3));
        case 'note_pair'
            d = mididevice('JGraph Loopback');
            midisend(d, midimsg('Note', 2, 64, 90, 0.2));
            pause(0.5);
            m = midireceive(d);
            out = sprintf('%d | %s %s | %d %d | %d', numel(m), mat2str(m(1).MsgBytes), mat2str(m(2).MsgBytes), m(1).Velocity, m(2).Velocity, ...
                m(2).Timestamp - m(1).Timestamp > 0.1);
        case 'args'
            d = mididevice('JGraph Loopback');
            midisend(d, 'ProgramChange', 5, 42);
            midisend(d, midimsg('PitchBend', 1, 8192));
            midisend(d, {midimsg('TuneRequest'), midimsg('ActiveSensing')});
            pause(0.3);
            m = midireceive(d);
            out = [strjoin(cellstr([m.Type]), ' ') ' | ' sprintf('%s;', string([m.Type]))];
        case 'sysex'
            d = mididevice('JGraph Loopback');
            midisend(d, midimsg('SystemExclusive', 1:10, 0));
            pause(0.3);
            m = midireceive(d);
            parts = arrayfun(@(x) sprintf('%s %s', char(x.Type), mat2str(x.MsgBytes)), m, 'UniformOutput', false);
            out = strjoin(parts', '; ');
        case 'sink'
            d = mididevice('JGraph Sink');
            midisend(d, [midimsg('NoteOn', 1, 60, 100); midimsg('ControlChange', 16, 7, 127); midimsg('SystemExclusive', [67 16], 0); midimsg('Stop')]);
            pause(0.3);
            sent = jgraph.internal.midisim('sent');
            out = strjoin(sent', '; ');
        case 'sink_is_output'
            d = mididevice('JGraph Sink');
            out = [sprintf('%d %d %s|', d.InputID, d.OutputID, d.Output) dv_err(@() midireceive(d)) ' // ' dv_err(@() hasdata(d))];
        case 'input_only'
            d = mididevice('Input', 'JGraph Loopback');
            out = [sprintf('%d %d|', d.InputID, d.OutputID) dv_err(@() midisend(d, 'Start'))];
        case 'four_args'
            d = mididevice('Output', 'JGraph Sink', 'Input', 'JGraph Loopback');
            out = sprintf('%d %d %s %s', d.InputID, d.OutputID, d.Input, d.Output);
        case 'deleted'
            d = mididevice('JGraph Loopback');
            delete(d);
            out = [sprintf('%d|', isvalid(d)) dv_err(@() midireceive(d)) ' // ' dv_err(@() midisend(d, 'Start'))];
        case 'controls'
            mc = midicontrols(1007, 0.5);
            d = mididevice('JGraph Loopback');
            midisend(d, 'ControlChange', 1, 7, 100);
            midisend(d, 'ControlChange', 2, 7, 10);      % another channel: not this control
            midisend(d, 'ControlChange', 1, 8, 20);      % another control
            pause(0.3);
            [v, last] = read(mc);
            out = sprintf('%.6f %d | %s', v, last, ix_flat(evalc('disp(mc)')));
        case 'controls_raw'
            mc = midicontrols([1007 1008], [1 2], 'OutputMode', 'rawmidi');
            d = mididevice('JGraph Loopback');
            midisend(d, 'ControlChange', 1, 8, 99);
            pause(0.3);
            out = mat2str(midiread(mc));
        case 'controls_any'
            mc = midicontrols;
            d = mididevice('JGraph Loopback');
            midisend(d, 'ControlChange', 2, 10, 64);
            pause(0.3);
            [v, last] = read(mc);
            out = sprintf('%g %d', v, last);
        case 'controls_any_channel'
            mc = midicontrols(21);
            d = mididevice('JGraph Loopback');
            midisend(d, 'ControlChange', 9, 21, 127);
            pause(0.3);
            out = sprintf('%g', midiread(mc));
        case 'sync'
            % midisync sends to the output of the input's name, and the loopback brings the value back.
            mc = midicontrols([1003 1004], 0);
            midisync(mc, [0.25 1]);
            pause(0.3);
            v1 = midiread(mc);
            midisync(mc);
            pause(0.3);
            out = sprintf('%s %s | %s', mat2str(v1, 6), mat2str(midiread(mc), 6), strjoin(jgraph.internal.midisim('sent')', '; '));
        case 'sync_any'
            mc = midicontrols;
            midisync(mc, 0.5);
            pause(0.2);
            out = sprintf('%d', numel(jgraph.internal.midisim('sent')));
        case 'callback'
            mc = midicontrols(1007);
            midi_sim_count('reset');
            midicallback(mc, @(h) midi_sim_count(midiread(h)));
            d = mididevice('JGraph Loopback');
            midisend(d, 'ControlChange', 1, 7, 64);
            pause(0.4);
            midisend(d, 'ControlChange', 1, 7, 64);       % the same value: no call
            pause(0.4);
            midisend(d, 'ControlChange', 1, 8, 5);        % another control: no call
            pause(0.4);
            [count, value] = midi_sim_count();
            midicallback(mc, []);
            midisend(d, 'ControlChange', 1, 7, 1);
            pause(0.4);
            out = sprintf('%d %g %d', count, value, midi_sim_count());
        case 'device_name'
            lastwarn('');
            mc = midicontrols(7, 'MIDIDevice', 'Loop');
            [~, id] = lastwarn;
            [~, dev] = midiinfo(mc);
            out = sprintf('%s|%s|%s', id, dev, ix_flat(evalc('disp(mc)')));
        case 'midiid'
            d = mididevice('JGraph Loopback');
            midisend(d, midimsg('ControlChange', 3, 21, 5, 0.4));
            text = evalc('[c, name] = midiid;');
            out = sprintf('%d %s | %s', c, name, ix_flat(text));
        otherwise
            error('midi_sim_case:unknown', 'unknown case %s', kind);
    end
catch e
    out = ['ERR ' e.identifier ' ' e.message];
end
jgraph.internal.midisim('off');
out = strrep(out, '|', '/'); % a fixture value holds no |
end
