% probe_vrjoystick: whether vrjoystick is licensed and runs here, and what it says with no joystick.
% (vrjoystick(0) crashes R2025b with an access violation, so it is not asked.)
dv_pr('license_sl3d', 'license(''test'', ''Virtual_Reality_Toolbox'')');
dv_pr('which', 'which(''vrjoystick'')');
lastwarn('');
dv_pr('ctor1', 'vrjoystick(1)');
[m, id] = lastwarn;
dv_pr('ctor1_warn', 'sprintf(''%s ## %s'', id, m)');
dv_pr('ctor2', 'vrjoystick(2)');
dv_pr('ctor_none', 'vrjoystick');
dv_pr('ctor_ff', 'vrjoystick(1, ''forcefeedback'')');
dv_pr('ctor_bad_opt', 'vrjoystick(1, ''bogus'')');
dv_pr('ctor_str', 'vrjoystick(''a'')');
dv_pr('ctor_frac', 'vrjoystick(1.5)');
dv_pr('ctor_neg', 'vrjoystick(-1)');
dv_pr('ctor_three', 'vrjoystick(1, ''forcefeedback'', 3)');
dv_pr('methods', 'methods(''vrjoystick'')');
dv_pr('which_sim3d', 'which(''sim3d.io.Joystick'')');
