% midi_tune.m -- a short MIDI piece: harp arpeggios over a warm pad and a fretless bass,
% Am - F - C - G, closing on a soft Am(add9). About ten seconds.
%
% Runs in JGraph and in MATLAB with Audio Toolbox. It plays on the Microsoft GS Wavetable Synth
% when there is one (every Windows machine has it), or else on the first MIDI output mididevinfo
% lists. Every note is built with midimsg and the whole piece goes out in one midisend: each
% message's Timestamp is its delay in seconds, so midisend returns at once and the notes play on
% time while the script waits.
%
% To use your own instruments, change the program numbers (General MIDI, counted from 0):
% 46 Orchestral Harp, 89 Pad 2 (warm), 35 Fretless Bass. Try 4 (Electric Piano), 10 (Music Box)
% or 11 (Vibraphone) for the arpeggios.

info = mididevinfo;
if isempty(info.output)
    error('No MIDI output on this machine.');
end
names = {info.output.Name};
if any(strcmp(names, 'Microsoft GS Wavetable Synth'))
    device = mididevice('Microsoft GS Wavetable Synth');
else
    device = mididevice(info.output(1).ID);
end
disp(device)

msgs = tune();
fprintf('Playing %d messages over %.1f seconds...\n', numel(msgs), max([msgs.Timestamp]));
midisend(device, msgs);
pause(max([msgs.Timestamp]) + 0.8);    % let the last chord ring before the device closes
clear device
disp('Done.')

function msgs = tune()
% The piece as one column of midimsgs, each timestamped from the start.
beat = 0.22;                                        % one arpeggio step, in seconds
bar = 8 * beat;
chords = {[57 60 64], [53 57 60], [48 52 55], [55 59 62]};    % Am, F, C, G
bass = [45 41 36 43];

msgs = [midimsg('ProgramChange', 1, 46, 0);         % channel 1: Orchestral Harp
        midimsg('ProgramChange', 2, 89, 0);         % channel 2: Pad 2 (warm)
        midimsg('ProgramChange', 3, 35, 0);         % channel 3: Fretless Bass
        midimsg('ControlChange', 1, 91, 70, 0);     % reverb on the harp
        midimsg('ControlChange', 2, 91, 90, 0);     % and more on the pad,
        midimsg('ControlChange', 2, 7, 70, 0)];     % which sits back a little

t0 = 0.3;
for k = 1:numel(chords)
    c = chords{k};
    t = t0 + (k - 1) * bar;

    % Harp: up through the chord, an octave above, and back.
    arp = [c, c + 12, c(1) + 24, c(2) + 12];
    for j = 1:numel(arp)
        velocity = 88 - 3 * j;
        msgs = [msgs; midimsg('Note', 1, arp(j), velocity, beat * 1.8, t + (j - 1) * beat)]; %#ok<AGROW>
    end

    % Pad: the chord held for the bar.
    for n = c
        msgs = [msgs; midimsg('Note', 2, n, 55, bar * 0.98, t)]; %#ok<AGROW>
    end

    % Bass: the root.
    msgs = [msgs; midimsg('Note', 3, bass(k), 80, bar * 0.9, t)]; %#ok<AGROW>
end

% The close: a quick harp strum of Am(add9), the pad under it, a low A.
t = t0 + numel(chords) * bar;
strum = [57 64 67 71 72 76];
for j = 1:numel(strum)
    msgs = [msgs; midimsg('Note', 1, strum(j), 70, 2.4, t + (j - 1) * 0.06)]; %#ok<AGROW>
end
for n = [57 60 64 71]
    msgs = [msgs; midimsg('Note', 2, n, 50, 2.6, t)]; %#ok<AGROW>
end
msgs = [msgs; midimsg('Note', 3, 33, 75, 2.4, t)];
end
