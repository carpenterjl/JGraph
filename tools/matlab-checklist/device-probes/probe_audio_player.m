% PROBE_AUDIO_PLAYER  audioplayer's validation, callbacks and timing in R2025b (device classes plan,
%   stage D10). Every signal played is zeros, so nothing is heard; nothing records.
global LOG
LOG = {};
dv_pr('c_none', 'audioplayer()')
dv_pr('c_one', 'audioplayer(zeros(10,1))')
dv_pr('c_five', 'audioplayer(zeros(10,1), 8000, 16, -1, 5)')
dv_pr('c_char', 'audioplayer(''abc'', 8000)')
dv_pr('c_fs_char', 'audioplayer(zeros(10,1), ''8000'')')
dv_pr('c_empty', 'audioplayer([], 8000)')
dv_pr('c_three_ch', 'audioplayer(zeros(10,3), 8000)')
dv_pr('c_fs_low', 'audioplayer(zeros(10,1), 79)')
dv_pr('c_fs_high', 'audioplayer(zeros(10,1), 1e6+1)')
dv_pr('c_fs_vec', 'audioplayer(zeros(10,1), [8000 8000])')
dv_pr('c_fs_inf', 'audioplayer(zeros(10,1), Inf)')
dv_pr('c_bits', 'audioplayer(zeros(10,1), 8000, 12)')
dv_pr('c_bits_vec', 'audioplayer(zeros(10,1), 8000, [8 16])')
dv_pr('c_dev_bad', 'audioplayer(zeros(10,1), 8000, 16, 99)')
dv_pr('c_dev_in', 'audioplayer(zeros(10,1), 8000, 16, 0)')
dv_pr('c_dev_vec', 'audioplayer(zeros(10,1), 8000, 16, [3 4])')
dv_pr('c_int32', 'audioplayer(int32(zeros(10,1)), 8000)')
dv_pr('c_logical', 'audioplayer(false(10,1), 8000)')
dv_pr('c_cplx', 'audioplayer(complex(zeros(10,1)), 8000)')
dv_pr('c_sparse', 'audioplayer(sparse(zeros(10,1)), 8000)')
dv_pr('c_3d', 'audioplayer(zeros(10,1,2), 8000)')
dv_pr('c_row', 'size(audioplayer(zeros(1,10), 8000).TotalSamples)')
dv_pr('row_total', 'get(audioplayer(zeros(1,10), 8000), ''TotalSamples'')')
dv_pr('row2_ch', 'get(audioplayer(zeros(2,10), 8000), ''NumChannels'')')
dv_pr('bits_single', 'get(audioplayer(single(zeros(10,1)), 8000), ''BitsPerSample'')')
dv_pr('bits_int16', 'get(audioplayer(int16(zeros(10,1)), 8000), ''BitsPerSample'')')
dv_pr('bits_uint8', 'get(audioplayer(uint8(zeros(10,1)), 8000), ''BitsPerSample'')')
dv_pr('bits_int8', 'get(audioplayer(int8(zeros(10,1)), 8000), ''BitsPerSample'')')
dv_pr('bits_24', 'get(audioplayer(zeros(10,1), 8000, 24), ''BitsPerSample'')')
dv_pr('bits_8_double', 'get(audioplayer(zeros(10,1), 8000, 8), ''BitsPerSample'')')
dv_pr('dev_out', 'get(audioplayer(zeros(10,1), 8000, 16, 3), ''DeviceID'')')
dv_pr('fs_frac', 'get(audioplayer(zeros(10,1), 8000.5), ''SampleRate'')')
dv_pr('fs_int', 'class(get(audioplayer(zeros(10,1), int16(8000)), ''SampleRate''))')

p = audioplayer(zeros(4000, 2), 8000);
dv_px('disp', 'p')
dv_px('disp_get', 'get(p)')
dv_px('disp_set', 'set(p)')
dv_pr('get_tag', 'get(p, ''Tag'')')
dv_pr('get_cell', 'get(p, {''Tag'', ''SampleRate''})')
dv_pr('get_trunc', 'get(p, ''samp'')')
dv_pr('get_case', 'p.samplerate')
dv_pr('get_bogus', 'get(p, ''Bogus'')')
dv_pr('dot_bogus', 'p.Bogus')
dv_px('set_tag', 'set(p, ''Tag'', "hello"); disp(class(p.Tag))')
dv_px('set_tag_num', 'p.Tag = 5;')
dv_px('set_tag_strarr', 'p.Tag = ["a" "b"];')
dv_px('set_fs', 'p.SampleRate = 16000; disp(p.SampleRate)')
dv_px('set_fs_bad', 'p.SampleRate = 10;')
dv_px('set_fs_char', 'p.SampleRate = ''a'';')
dv_px('set_bits', 'p.BitsPerSample = 8;')
dv_px('set_total', 'p.TotalSamples = 8;')
dv_px('set_type', 'p.Type = ''x'';')
dv_px('set_period', 'p.TimerPeriod = 0.0001;')
dv_px('set_period_neg', 'p.TimerPeriod = -1;')
dv_px('set_period_vec', 'p.TimerPeriod = [1 2];')
dv_px('set_fcn_num', 'p.StartFcn = 5;')
dv_px('set_fcn_char', 'p.StartFcn = ''disp(1)''; disp(class(p.StartFcn))')
dv_px('set_fcn_cell', 'p.StartFcn = {@disp, 1}; disp(class(p.StartFcn))')
dv_px('set_fcn_empty', 'p.StartFcn = []; disp(isempty(p.StartFcn))')
dv_px('set_ud', 'p.UserData = {1,2}; disp(class(p.UserData))')
dv_px('set_bogus', 'set(p, ''Bogus'', 1)')
dv_px('set_pair_odd', 'set(p, ''Tag'')')
dv_pr('set_one', 'set(p, ''Tag'')')
dv_px('horz', '[p p]')
dv_px('horz1', 'q = [p]; disp(class(q))')
dv_pr('isplaying0', 'isplaying(p)')
dv_px('play_bad_idx', 'play(p, [1 2 3])')
dv_px('play_char_idx', 'play(p, ''a'')')
dv_px('play_empty_idx', 'play(p, [])')
dv_px('play_three', 'play(p, 1, 2)')
dv_px('play_sel', 'play(p, [5 2]); disp(isplaying(p)); stop(p)')
dv_px('play_sel0', 'play(p, 0); stop(p)')

% Callbacks and timing on a silent half second.
q = audioplayer(zeros(4000, 1), 8000);
q.StartFcn = @(o, e) probe_audio_log('start', o, e);
q.StopFcn = @(o, e) probe_audio_log('stop', o, e);
q.TimerFcn = @(o, e) probe_audio_log('timer', o, e);
q.TimerPeriod = 0.1;
LOG = {};
t0 = tic;
play(q);
dv_pr('after_play_running', 'q.Running')
dv_pr('after_play_log', 'LOG')
dv_pr('after_play_cur', 'q.CurrentSample > 0')
pause(1);
dv_pr('after_pause_log', 'LOG')
dv_pr('after_pause_running', 'q.Running')
dv_pr('after_pause_cur', 'q.CurrentSample')
LOG = {};
playblocking(q);
dv_pr('blocking_took', 'round(toc(t0)*10)/10 > 0')
dv_pr('blocking_log', 'LOG')
dv_pr('blocking_cur', 'q.CurrentSample')
LOG = {};
play(q); pause(0.2); pause(q);
dv_pr('paused_log', 'LOG')
dv_pr('paused_cur_mid', 'q.CurrentSample > 1 && q.CurrentSample < 4000')
dv_pr('paused_running', 'q.Running')
LOG = {};
resume(q); pause(0.05);
dv_pr('resumed_log', 'LOG')
stop(q);
dv_pr('stopped_log', 'LOG')
dv_pr('stopped_cur', 'q.CurrentSample')
LOG = {};
stop(q);
dv_pr('stop_again_log', 'LOG')
q.StartFcn = 'global LOG; LOG{end+1} = ''charstart'';';
q.StopFcn = {@probe_audio_cell, 'cellstop'};
q.TimerFcn = [];
LOG = {};
playblocking(q, [1 800]);
dv_pr('char_cell_log', 'LOG')
q.StartFcn = @(o, e) error('my:id', 'boom');
q.StopFcn = [];
dv_px('start_error', 'play(q); pause(0.1); disp(q.Running); stop(q)')
delete(q);
dv_pr('deleted_valid', 'isvalid(q)')
dv_pr('deleted_get', 'q.SampleRate')

% sound and soundsc, silent.
dv_px('sound_zero', 'sound(zeros(100,1))')
dv_px('sound_int', 'sound(int16(zeros(100,1)))')
dv_px('sound_cplx', 'sound(complex(zeros(100,1)))')
dv_px('sound_none', 'sound()')
dv_px('sound_empty_fs', 'sound(zeros(10,1), [])')
dv_px('sound_empty_bits', 'sound(zeros(10,1), 8000, [])')
dv_px('sound_empty', 'sound([])')
dv_px('sound_3d', 'sound(zeros(2,2,2))')
dv_px('sound_bad_fs', 'sound(zeros(10,1), 10)')
dv_px('sound_three_ch', 'sound(zeros(10,3), 8000)')
dv_px('soundsc_none', 'soundsc()')
dv_px('soundsc_int', 'soundsc(int16(1:10))')
dv_px('soundsc_slim', 'soundsc(zeros(10,1), [1 0])')
dv_px('soundsc_inf', 'soundsc([0 Inf 0])')
dv_px('soundsc_nan', 'soundsc([0 NaN 0])')
dv_px('soundsc_zero', 'soundsc(zeros(10,1))')
dv_px('reset', 'audiodevreset')
dv_pr('reset_out', 'numel(audiodevinfo().output)')
