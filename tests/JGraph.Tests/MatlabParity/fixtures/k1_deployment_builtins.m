% record: -noFigureWindows
% Compiler and SDK plan, stage K1: the deployment builtins in an ordinary session - isdeployed,
% ismcc, ctfroot, mcrversion, mcrcachedir, getmcruserdata and setmcruserdata, with their refusals
% (probe_k1_session in tools/matlab-checklist/compiler-probes) - and three answers K1 fixed on the
% way: s = beep answers the switch, quit's 'force', and a call of nothing whose arguments expand to
% none. What a deployed session does is DeployedSessionTests' and the compiler probes' (K0, K1).

u2_chk('isdeployed', @() isdeployed);
u2_chk('isdeployed_class', @() class(isdeployed));
u2_chk('ismcc', @() ismcc);
u2_chk('isdeployed_arg', @() isdeployed(1));
u2_chk('ismcc_arg', @() ismcc(1));
u2_chk('ctfroot_is_matlabroot', @() strcmp(ctfroot, matlabroot));
u2_chk('ctfroot_arg', @() ctfroot(1));
u2_chk('mcrversion_bare', @() mat2str(mcrversion));
u2_chk('mcrversion_1', @() mat2str(versions(1)));
u2_chk('mcrversion_2', @() mat2str(versions(2)));
u2_chk('mcrversion_3', @() mat2str(versions(3)));
u2_chk('mcrversion_5', @() mat2str(versions(5)));
u2_chk('mcrversion_class', @() class(mcrversion));
u2_chk('mcrversion_arg', @() mcrversion(1));
u2_chk('mcrcachedir_class', @() class(mcrcachedir));
u2_chk('mcrcachedir_local', @() startsWith(mcrcachedir, getenv('LOCALAPPDATA'), 'IgnoreCase', true));
u2_chk('mcrcachedir_arg', @() mcrcachedir(1));

u2_chk('get_missing_size', @() mat2str(size(getmcruserdata('k1nosuch'))));
u2_chk('get_missing_class', @() class(getmcruserdata('k1nosuch')));
u2_chk('get_missing_string_key', @() mat2str(size(getmcruserdata("k1nosuch"))));
u2_chk('get_noargs', @() getmcruserdata());
u2_chk('get_two', @() getmcruserdata('a', 'b'));
u2_chk('get_number_key', @() getmcruserdata(5));
setmcruserdata('k1key', 'k1value');
u2_chk('get_char', @() getmcruserdata('k1key'));
setmcruserdata('k1num', [1 2 3]);
u2_chk('get_double', @() mat2str(getmcruserdata('k1num')));
setmcruserdata('k1s', struct('a', 1));
u2_chk('get_struct', @() fieldof(getmcruserdata('k1s')));
setmcruserdata('k1key', 7);
u2_chk('get_replaced', @() getmcruserdata('k1key'));
u2_chk('get_other_case', @() mat2str(size(getmcruserdata('K1KEY'))));
setmcruserdata('k1key', []);
u2_chk('get_set_empty', @() mat2str(size(getmcruserdata('k1key'))));
u2_chk('set_one', @() setone());
u2_chk('set_three', @() setthree());
u2_chk('set_number_key', @() setnumber());
setmcruserdata("k1str", 3);
u2_chk('get_string_key', @() getmcruserdata('k1str'));
u2_chk('set_output', @() setout());
u2_chk('waitforallfiguresclosed', @() waitforallfiguresclosed());

% The state a session starts in is the host's (R2025b's -batch starts off, a compiled program on).
was = beep;
beep('off');
u2_chk('beep_off', @() beep);
beep(was);

u2_chk('undefined_expanded_none', @() callnothing());
u2_chk('feval_undefined_arg', @() feval('k1_nosuch_fn', 2));
u2_chk('feval_undefined_bare', @() feval('k1_nosuch_fn'));

function c = versions(n)
c = cell(1, n);
[c{:}] = mcrversion;
c = [c{:}];
end

function v = fieldof(s)
v = s.a;
end

function r = setone()
setmcruserdata('k1key');
r = 'ran';
end

function r = setthree()
setmcruserdata('a', 1, 2);
r = 'ran';
end

function r = setnumber()
setmcruserdata(5, 1);
r = 'ran';
end

function r = setout()
r = setmcruserdata('k1o', 1);
end

function r = callnothing(varargin)
r = k1_nosuch_fn(varargin{:});
end
