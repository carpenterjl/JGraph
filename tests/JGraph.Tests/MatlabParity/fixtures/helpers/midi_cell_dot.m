function t = midi_cell_dot()
% MIDI_CELL_DOT  A midimsg in a cell, written through the cell: c{1}.Timestamp = 2 (stage D10b).
c = {midimsg('Start')};
c{1}.Timestamp = 2;
t = c{1}.Timestamp;
end
