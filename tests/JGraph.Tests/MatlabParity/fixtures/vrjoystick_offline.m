% vrjoystick_offline.m -- vrjoystick with no joystick attached (device classes plan, stage D6): the
% constructor's argument counts, the deprecation warning its first construction raises, and
% notconnected for every id and option when nothing answers. vrjoystick(0) crashes R2025b with an
% access violation, so it is not asked. Recorded from R2025b on a machine with no game controller;
% JGraph reads WinMM's joysticks, of which that machine has none either.

% Too many inputs are refused before the body runs, so with no warning; the body warns once a
% session, before it finds the id missing.
lastwarn('');
ix_chk('three', dv_err(@() vrjoystick(1, 'forcefeedback', 3)));
[m, id] = lastwarn;
ix_chk('three_no_warning', ['[' id ']']);
ix_chk('none', dv_err(@() vrjoystick()));
[m, id] = lastwarn;
ix_chk('first_warning', [id ' ## ' m]);
lastwarn('');
ix_chk('id1', dv_err(@() vrjoystick(1)));
[m, id] = lastwarn;
ix_chk('id1_no_second_warning', ['[' id ']']);
ix_chk('id2', dv_err(@() vrjoystick(2)));
ix_chk('id_big', dv_err(@() vrjoystick(99)));
ix_chk('id_text', dv_err(@() vrjoystick('a')));
ix_chk('id_frac', dv_err(@() vrjoystick(1.5)));
ix_chk('id_neg', dv_err(@() vrjoystick(-1)));
ix_chk('id_vector', dv_err(@() vrjoystick([1 2])));
ix_chk('forcefeedback', dv_err(@() vrjoystick(1, 'forcefeedback')));
ix_chk('bad_option', dv_err(@() vrjoystick(1, 'bogus')));
