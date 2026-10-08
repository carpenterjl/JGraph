% Open items 15, 17, 21, 23, 28, 30, 53 and 73 (ADR 0215): language forms. Command syntax passes
% char words; @name for a name nothing answers is a handle refused only when called; an unsuppressed
% multiple assignment echoes each target; hexadecimal and binary literals are integers of the class
% their value or suffix gives; a list written as a statement shows each value; MATLAB has no e. The
% echoes are compared by the names they show, since the two engines lay a value out differently.
% Probe probe_b5 (open-items scratch).

% --- command syntax (item 15) ---
u9b_chk('cmd_class', @() command_class());
u9b_chk('cmd_upper', @() command_upper());

% --- a handle to a name nothing answers (items 17, 73) ---
f = @no_such_fn_oi;
u9b_chk('late_class', @() class(f));
u9b_chk('late_func2str', @() func2str(f));
u9b_chk('late_call', @() f());
u9b_chk('late_feval', @() feval(f, 1));
u9b_chk('late_isequal', @() isequal(f, @no_such_fn_oi));
fn = functions(f);
u9b_chk('late_functions', @() {fn.function, fn.type, fn.file});

% --- the echo of a multiple assignment (item 21) ---
u9b_chk('echo_two', @() echoed('[a1, b1] = deal(1, 2)'));
u9b_chk('echo_tilde', @() echoed('[a2, ~] = deal(3, 4)'));
u9b_chk('echo_indexed', @() echoed('st = struct(); cc = cell(1, 2); [st.x, cc{2}] = deal(5, 6)'));
u9b_chk('echo_list', @() echoed('c3 = cell(1, 2); [c3{:}] = deal(7, 8)'));
u9b_chk('echo_suppressed', @() echoed('[a4, b4] = deal(1, 2);'));
c5 = cell(1, 2);
[c5{:}] = deal(7, 8);
u9b_chk('list_target_values', @() c5);

% --- hexadecimal and binary literals (item 23) ---
u9b_chk('hex_plain', @() 0x1F);
u9b_chk('hex_upper', @() 0X1f);
u9b_chk('bin_plain', @() 0b101);
u9b_chk('hex_16', @() 0x1FFFF);
u9b_chk('hex_u8', @() 0xFFu8);
u9b_chk('hex_s8', @() 0x7Fs8);
u9b_chk('hex_s8_negative', @() 0x80s8);
u9b_chk('hex_32', @() 0xFFFFFFFF);
u9b_chk('hex_64', @() 0x100000000);
u9b_chk('bin_u16', @() 0b11111111u16);
u9b_chk('hex_s32', @() 0x1Fs32);
u9b_chk('hex_negated', @() -0x1F);
u9b_chk('hex_plus', @() 0x1F + 1);
u9b_chk('hex_s64', @() class(0x1Fs64));

% --- an anonymous function asked for nothing (item 28) ---
u9b_chk('anon_none_answers', @() echoed('a28 = @() max([1 2]); a28()'));
u9b_chk('anon_none_disp', @() echoed('b28 = @() disp(5); b28()'));

% --- a list written as a statement (item 30) ---
st30 = struct('f', {1, 2.5});
u9b_chk('list_statement', @() echoed('st30 = struct(''f'', {1, 2.5}); st30.f'));
c30 = {3, 4};
u9b_chk('list_brace_statement', @() echoed('c30 = {3, 4}; c30{:}'));
u9b_chk('list_operator', @() st30.f + 1);
u9b_chk('list_disp', @() disp_list(st30));

% --- e (item 53) ---
u9b_chk('no_e', @() e_value());

function w = command_class()
class abc;
w = ans;
end

function w = command_upper()
upper abc;
w = ans;
end

function names = echoed(code)
% The names an echo shows, in order, as "name =" lines.
out = evalc(code);
names = regexp(out, '(\w+) =', 'tokens');
names = [names{:}];
end

function id = disp_list(s)
id = '';
try
    disp(s.f);
catch err
    id = err.identifier;
end
end

function v = e_value()
v = e;
end
