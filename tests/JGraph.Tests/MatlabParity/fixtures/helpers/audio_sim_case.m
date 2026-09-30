function out = audio_sim_case(kind)
% AUDIO_SIM_CASE  The cases of audio_sim (device classes plan, stage D10, ADR 0193): recordings and
%   playback on jgraph.internal.audiosim's devices, whose microphones give a 440 Hz sine of amplitude
%   0.5 from sample 0 and whose outputs keep the last block they played. The answers are written from
%   the rule: what audiorecorder.m, getaudiodata's conversions, sound.m and soundsc.m make of those
%   samples. Answers text: a value, or an error's identifier.
jgraph.internal.audiosim('on');
try
    switch kind
        case 'list'
            info = audiodevinfo;
            out = sprintf('%s; ', info.input.Name, info.output.Name);
        case 'ids'
            info = audiodevinfo;
            out = mat2str([info.input.ID info.output.ID]);
        case 'block'
            r = audiorecorder(8000, 16, 1);
            recordblocking(r, 0.25);
            y = getaudiodata(r);
            out = sprintf('%d %d %s %s %d %d %g', r.TotalSamples, r.CurrentSample, r.Running, class(y), size(y, 1), size(y, 2), max(y));
        case 'types'
            r = audiorecorder(8000, 16, 1);
            recordblocking(r, 0.1);
            out = sprintf('%s %d; %s %d; %s %d; %s %g', class(getaudiodata(r, 'int16')), max(getaudiodata(r, 'int16')), ...
                class(getaudiodata(r, 'uint8')), max(getaudiodata(r, 'uint8')), class(getaudiodata(r, 'int8')), max(getaudiodata(r, 'int8')), ...
                class(getaudiodata(r, 'single')), max(getaudiodata(r, 'single')));
        case 'eight_bit'
            r = audiorecorder(8000, 8, 1);
            recordblocking(r, 0.1);
            y = getaudiodata(r, 'uint8');
            out = sprintf('%s %d %d', class(y), max(y), min(y));
        case 'stereo'
            r = audiorecorder(8000, 8, 2);
            recordblocking(r, 0.1);
            out = mat2str(size(getaudiodata(r)));
        case 'timed'
            r = audiorecorder(8000, 16, 1);
            record(r, 0.2);
            pause(0.6);
            out = sprintf('%s %d %d', r.Running, r.TotalSamples, r.CurrentSample);
        case 'pause_resume'
            r = audiorecorder(8000, 16, 1);
            record(r);
            pause(0.15);
            pause(r);
            t1 = r.TotalSamples;
            a = sprintf('%s %d %d', r.Running, t1 > 0, r.CurrentSample == t1 + 1);
            resume(r);
            pause(0.1);
            stop(r);
            out = sprintf('%s; %s %d %d', a, r.Running, r.TotalSamples > t1, r.CurrentSample);
        case 'record_again'
            r = audiorecorder(8000, 16, 1);
            recordblocking(r, 0.1);
            recordblocking(r, 0.05);
            out = sprintf('%d', r.TotalSamples);
        case 'callbacks'
            audio_log('clear');
            r = audiorecorder(8000, 16, 1);
            r.StartFcn = @(o, e) audio_log('start', o, e);
            r.StopFcn = @(o, e) audio_log('stop', o, e);
            recordblocking(r, 0.1);
            out = audio_log();
        case 'play_recording'
            r = audiorecorder(8000, 16, 1);
            recordblocking(r, 0.1);
            p = play(r);
            out = sprintf('%s %s %d %d %d', class(p), p.Running, p.TotalSamples, p.BitsPerSample, p.SampleRate);
            stop(p);
        case 'getplayer'
            r = audiorecorder(11025, 8, 2);
            recordblocking(r, 0.05);
            p = getplayer(r);
            out = sprintf('%d %d %d %d %s', p.SampleRate, p.BitsPerSample, p.NumChannels, p.TotalSamples == r.TotalSamples, p.Running);
        case 'played_player'
            p = audioplayer(int16([16384; -16384; 0]), 8000);
            playblocking(p);
            y = jgraph.internal.audiosim('played');
            out = sprintf('%d %g %g %g', size(y, 1), y(1), y(2), y(3));
        case 'played_uint8'
            p = audioplayer(uint8([192; 64; 128]), 8000);
            playblocking(p);
            y = jgraph.internal.audiosim('played');
            out = sprintf('%g %g %g', y(1), y(2), y(3));
        case 'played_selection'
            p = audioplayer((1:1000)' / 1000, 8000);
            playblocking(p, [101 300]);
            y = jgraph.internal.audiosim('played');
            out = sprintf('%d %g %g %g', size(y, 1), y(1), y(200), y(201));
        case 'sound_padded'
            sound(0.25 * ones(800, 2), 8000);
            pause(0.2);
            y = jgraph.internal.audiosim('played');
            out = sprintf('%s %g %g', mat2str(size(y)), y(1, 1), y(end, 1));
        case 'sound_clipped'
            sound([2; -2; 0.5], 8000);
            pause(0.1);
            y = jgraph.internal.audiosim('played');
            out = sprintf('%g %g %g', y(1), y(2), y(3));
        case 'sound_row'
            sound([0.1 0.2 0.3], 8000);
            pause(0.1);
            y = jgraph.internal.audiosim('played');
            out = sprintf('%d %g', size(y, 2), y(3));
        case 'soundsc_scaled'
            soundsc([0; 1; 2; 3], 8000);
            pause(0.1);
            y = jgraph.internal.audiosim('played');
            out = sprintf('%.4f %.4f %.4f %.4f', y(1:4));
        case 'soundsc_slim'
            soundsc([0; 1; 2], 8000, [0 2]);
            pause(0.1);
            y = jgraph.internal.audiosim('played');
            out = sprintf('%g %g %g', y(1:3));
        case 'soundsc_flat'
            soundsc([5; 5; 5], 8000);
            pause(0.1);
            y = jgraph.internal.audiosim('played');
            out = sprintf('%g %g %g', y(1:3));
        otherwise
            error('audio_sim_case:case', 'no case %s', kind);
    end
catch e
    out = e.identifier;
end
end
