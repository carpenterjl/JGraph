function t = midi_fresh()
% MIDI_FRESH  A midimsg stored into a variable that does not exist yet (device classes plan, stage D10b).
q(2) = midimsg('Start');
q(end+1) = midimsg('Stop');
t = sprintf('%s %d %d %s %s', class(q), size(q), char(q(1).Type), char(q(3).Type));
end
