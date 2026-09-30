function midi_read_two(mc)
% MIDI_READ_TWO  midiread asked for two outputs, which it does not have (stage D10b).
[v, c] = midiread(mc); %#ok<ASGLU>
end
