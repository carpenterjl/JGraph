% audio_recorder.m -- audiorecorder (device classes plan, stage D10): its constructor's checks, its
% properties and setters, and every refusal that comes before a recording starts. Recorded from
% R2025b, where nothing records: each call here that would open the microphone fails first. JGraph
% runs on jgraph.internal.audiosim's devices (audio_env); what a recording holds is audio_sim's.

audio_env();
ix_chk('c_one', dv_err(@() audiorecorder(8000)));
ix_chk('c_two', dv_err(@() audiorecorder(8000, 16)));
ix_chk('c_five', dv_err(@() audiorecorder(8000, 16, 1, -1, 5)));
ix_chk('c_fs_low', dv_err(@() audiorecorder(80, 16, 1)));
ix_chk('c_fs_81', sprintf('%g', get(audiorecorder(81, 16, 1), 'SampleRate')));
ix_chk('c_fs_high', dv_err(@() audiorecorder(1e6+1, 16, 1)));
ix_chk('c_fs_vec', dv_err(@() audiorecorder([8000 8000], 16, 1)));
ix_chk('c_fs_inf', dv_err(@() audiorecorder(Inf, 16, 1)));
ix_chk('c_fs_char', dv_err(@() audiorecorder('a', 16, 1)));
ix_chk('c_bits', dv_err(@() audiorecorder(8000, 12, 1)));
ix_chk('c_bits_vec', dv_err(@() audiorecorder(8000, [8 16], 1)));
ix_chk('c_ch3', dv_err(@() audiorecorder(8000, 16, 3)));
ix_chk('c_ch0', dv_err(@() audiorecorder(8000, 16, 0)));
ix_chk('c_ch_vec', dv_err(@() audiorecorder(8000, 16, [1 2])));
ix_chk('c_dev_bad', dv_err(@() audiorecorder(8000, 16, 1, 99)));
ix_chk('c_dev_out', dv_err(@() audiorecorder(8000, 16, 1, 3)));
ix_chk('c_dev_vec', dv_err(@() audiorecorder(8000, 16, 1, [0 1])));
ix_chk('c_dev_in', sprintf('%d', get(audiorecorder(8000, 16, 1, 1), 'DeviceID')));
ix_chk('c_24', sprintf('%d', get(audiorecorder(8000, 24, 1), 'BitsPerSample')));
ix_chk('c_frac', sprintf('%g', get(audiorecorder(8000.5, 16, 1), 'SampleRate')));

d = audiorecorder;
ix_chk('defaults', sprintf('%g %d %d %d %d %d %s %g %s', d.SampleRate, d.BitsPerSample, d.NumChannels, d.DeviceID, ...
    d.CurrentSample, d.TotalSamples, d.Running, d.TimerPeriod, d.Type));
ix_chk('class', class(d));
ix_chk('props', strjoin(properties(d)', ','));
ix_chk('hidden', sprintf('%d %d', d.NumberOfChannels, isempty(d.BufferLength)));
d.BufferLength = 5;
ix_chk('hidden_set', sprintf('%d', d.BufferLength));
ix_chk('dot_trunc', sprintf('%g', d.samp));

r = audiorecorder(8000, 16, 1);
ix_chk('empty_get', dv_err(@() getaudiodata(r)));
ix_chk('empty_get_type', dv_err(@() getaudiodata(r, 'int32')));
ix_chk('empty_get_num', dv_err(@() getaudiodata(r, 5)));
ix_chk('empty_get_str', dv_err(@() getaudiodata(r, "int16")));
ix_chk('empty_get_three', dv_err(@() getaudiodata(r, 'double', 1)));
ix_chk('empty_player', dv_err(@() getplayer(r)));
ix_chk('empty_play', dv_err(@() play(r)));
ix_chk('player_from_rec', dv_err(@() audioplayer(r, 3)));
ix_chk('player_from_rec_bad', dv_err(@() audioplayer(r, 'a')));
ix_chk('player_from_rec3', dv_err(@() audioplayer(r, 3, 4)));
ix_chk('rec_neg', dv_err(@() record(r, -1)));
ix_chk('rec_zero', dv_err(@() record(r, 0)));
ix_chk('rec_nan', dv_err(@() record(r, NaN)));
ix_chk('rec_char', dv_err(@() record(r, 'a')));
ix_chk('rec_empty', dv_err(@() record(r, [])));
ix_chk('rec_three', dv_err(@() record(r, 1, 2)));
ix_chk('recb_none', dv_err(@() recordblocking(r)));
ix_chk('recb_neg', dv_err(@() recordblocking(r, -1)));
ix_chk('state', sprintf('%d %s %d %d', isrecording(r), r.Running, r.CurrentSample, r.TotalSamples));
stop(r);
pause(r);
ix_chk('stop_idle', r.Running);
ix_chk('set_fs', dv_err(@() set(r, 'SampleRate', 16000)));
ix_chk('set_bits', dv_err(@() set(r, 'BitsPerSample', 8)));
ix_chk('set_period', dv_err(@() set(r, 'TimerPeriod', 0.0001)));
r.TimerPeriod = 0.2;
ix_chk('set_period_ok', sprintf('%g', r.TimerPeriod));
ix_chk('set_tag_num', dv_err(@() set(r, 'Tag', 1)));
ix_chk('set_fcn_num', dv_err(@() set(r, 'TimerFcn', 1)));
ix_chk('concat', dv_err(@() [r r]));
delete(r);
ix_chk('deleted', sprintf('%d', isvalid(r)));
