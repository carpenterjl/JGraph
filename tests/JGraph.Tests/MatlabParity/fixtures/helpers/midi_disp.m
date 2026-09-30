function t = midi_disp(v)
% MIDI_DISP  What disp shows for a value, as one line (ix_flat), with the multiplication sign of an
%   array's size written as x so the expected file stays ASCII (device classes plan, stage D10b).
t = strrep(ix_flat(evalc('disp(v)')), char(215), 'x');
end
