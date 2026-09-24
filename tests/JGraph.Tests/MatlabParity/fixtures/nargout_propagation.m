% nargout_propagation.m -- V9.1 of the value-ownership plan (ADR 0170): the output count a call
% asks for reaches the callee on every road - a statement call (zero), a value call (one), a
% multiple assignment (its target count), a handle, feval by handle and by name, an anonymous
% forward, cellfun/arrayfun/structfun, a method by dot and by name, a varargout relay, a nested
% function, eval/evalin, str2func, a handle held in a cell or a struct, builtin('feval', ...), and
% the expression contexts that ask for one (a condition, a loop head, a bracket, a field, an index,
% a '~'). What a statement call binds to ans: a direct call of a user function binds its first
% output when the function assigned it (a named output or varargout{1}) and nothing when it did
% not, without error; the same function through a handle, feval, an anonymous body or cellfun
% hands nothing back (measured); a plain builtin through a handle still hands back its value, a
% multi-output builtin (size) does not (measured). The unassigned-output refusal's identifier and
% wording on each road (#84). A case's value is the log of the nargout each callee saw.

run_case('np_stmt', @np_stmt);
run_case('np_bare', @np_bare);
run_case('np_value', @np_value);
run_case('np_bare_value', @np_bare_value);
run_case('handle_stmt', @handle_stmt);
run_case('handle_value', @handle_value);
run_case('handle_two', @handle_two);
run_case('feval_handle_stmt', @feval_handle_stmt);
run_case('feval_name_stmt', @feval_name_stmt);
run_case('feval_value', @feval_value);
run_case('feval_two', @feval_two);
run_case('anon_stmt', @anon_stmt);
run_case('anon_value', @anon_value);
run_case('anon_two', @anon_two);
run_case('cellfun_stmt', @cellfun_stmt);
run_case('cellfun_nonuniform_stmt', @cellfun_nonuniform_stmt);
run_case('cellfun_value', @cellfun_value);
run_case('cellfun_two', @cellfun_two);
run_case('arrayfun_stmt', @arrayfun_stmt);
run_case('arrayfun_value', @arrayfun_value);
run_case('structfun_stmt', @structfun_stmt);
run_case('structfun_value', @structfun_value);
run_case('method_stmt', @method_stmt);
run_case('method_fn_stmt', @method_fn_stmt);
run_case('method_value', @method_value);
run_case('method_two', @method_two);
run_case('relay_stmt', @relay_stmt);
run_case('relay_value', @relay_value);
run_case('relay_two', @relay_two);
run_case('nested_stmt_and_value', @nested_stmt_and_value);
run_case('ctx_if', @ctx_if);
run_case('ctx_while', @ctx_while);
run_case('ctx_switch', @ctx_switch);
run_case('ctx_for', @ctx_for);
run_case('ctx_bracket', @ctx_bracket);
run_case('ctx_brace', @ctx_brace);
run_case('ctx_plus', @ctx_plus);
run_case('ctx_field', @ctx_field);
run_case('ctx_cellassign', @ctx_cellassign);
run_case('ctx_indexassign', @ctx_indexassign);
run_case('ctx_tilde', @ctx_tilde);
run_case('ctx_tilde_two', @ctx_tilde_two);
run_case('ctx_argument', @ctx_argument);
run_case('eval_stmt', @eval_stmt);
run_case('evalin_stmt', @evalin_stmt);
run_case('str2func_stmt', @str2func_stmt);
run_case('cellheld_stmt', @cellheld_stmt);
run_case('structheld_stmt', @structheld_stmt);
run_case('builtin_feval_stmt', @builtin_feval_stmt);
run_case('builtin_feval_value', @builtin_feval_value);
run_case('nargout_query', @nargout_query);
run_case('ans_two_stmt', @ans_two_stmt);
run_case('ans_vo_sets_first_stmt', @ans_vo_sets_first_stmt);
run_case('ans_named_unassigned_stmt', @ans_named_unassigned_stmt);
run_case('ans_vo_none_stmt', @ans_vo_none_stmt);
run_case('ans_second_only_stmt', @ans_second_only_stmt);
run_case('ans_method_two_stmt', @ans_method_two_stmt);
run_case('ans_handle_np2_stmt', @ans_handle_np2_stmt);
run_case('ans_anon_two_stmt', @ans_anon_two_stmt);
run_case('ans_feval_two_stmt', @ans_feval_two_stmt);
run_case('ans_cellfun_stmt', @ans_cellfun_stmt);
run_case('ans_handle_vo_stmt', @ans_handle_vo_stmt);
run_case('ans_sin_handle_stmt', @ans_sin_handle_stmt);
run_case('ans_feval_sin_stmt', @ans_feval_sin_stmt);
run_case('ans_size_handle_stmt', @ans_size_handle_stmt);
run_case('ans_size_stmt', @ans_size_stmt);
run_case('ans_anon_expr_stmt', @ans_anon_expr_stmt);
run_case('ans_anon_sin_stmt', @ans_anon_sin_stmt);
run_case('unassigned_named', @unassigned_named);
run_case('unassigned_vo_none', @unassigned_vo_none);
run_case('unassigned_vo_short', @unassigned_vo_short);
run_case('unassigned_nested', @unassigned_nested);
run_case('unassigned_method', @unassigned_method);
run_case('unassigned_file', @unassigned_file);
run_case('unassigned_feval', @unassigned_feval);
run_case('unassigned_tilde', @unassigned_tilde);

function run_case(name, fn, rule)
if nargin < 3
    rule = 'exact';
end
global vlog_text
vlog_text = '';
try
    fprintf('CHK|%s|%s|%s\n', name, clean(fn()), rule);
catch err
    fprintf('CHK|%s|ERR %s|%s\n', name, clean(err.message), rule);
end
end

function s = clean(s)
if isnumeric(s) || islogical(s)
    s = mat2str(s);
end
s = char(s);
s = strrep(s, char(13), '');
s = strrep(s, char(10), ' ');
s = strrep(s, '|', '/');
end

function s = logged_text()
global vlog_text
s = vlog_text;
end

% --- the callees ------------------------------------------------------------------------------------

function y = np(varargin)
vlog(sprintf('%d', nargout));
y = 0;
end

function [a, b] = np2(varargin)
vlog(sprintf('%d', nargout));
a = 1;
b = 2;
end

function np0(varargin)
vlog(sprintf('%d', nargout));
end

function [a, b] = two()
vlog(sprintf('%d', nargout));
a = 1;
b = 2;
end

function varargout = vo_sets_first()
vlog(sprintf('%d', nargout));
varargout{1} = 5;
end

function varargout = vo_none() %#ok<STOUT>
vlog(sprintf('%d', nargout));
end

function y = named_unassigned() %#ok<STOUT>
vlog(sprintf('%d', nargout));
end

function [a, b] = second_only() %#ok<STOUT>
vlog(sprintf('%d', nargout));
b = 2;
end

function varargout = relay()
vlog(sprintf('relay%d', nargout));
[varargout{1:nargout}] = np2();
end

% --- the roads --------------------------------------------------------------------------------------

function s = np_stmt()
np();
s = logged_text();
end

function s = np_bare()
np;
s = logged_text();
end

function s = np_value()
v = np(); %#ok<NASGU>
s = logged_text();
end

function s = np_bare_value()
x = np; %#ok<NASGU>
s = logged_text();
end

function s = handle_stmt()
h = @np;
h();
s = logged_text();
end

function s = handle_value()
h = @np;
v = h(); %#ok<NASGU>
s = logged_text();
end

function s = handle_two()
h = @np2;
[a, b] = h(); %#ok<ASGLU>
s = logged_text();
end

function s = feval_handle_stmt()
feval(@np);
s = logged_text();
end

function s = feval_name_stmt()
feval('np');
s = logged_text();
end

function s = feval_value()
v = feval(@np); %#ok<NASGU>
s = logged_text();
end

function s = feval_two()
[a, b] = feval(@np2); %#ok<ASGLU>
s = logged_text();
end

function s = anon_stmt()
g = @() np();
g();
s = logged_text();
end

function s = anon_value()
g = @() np();
v = g(); %#ok<NASGU>
s = logged_text();
end

function s = anon_two()
g2 = @() np2();
[a, b] = g2(); %#ok<ASGLU>
s = logged_text();
end

function s = cellfun_stmt()
cellfun(@np, {1});
s = logged_text();
end

function s = cellfun_nonuniform_stmt()
cellfun(@np, {1}, 'UniformOutput', false);
s = logged_text();
end

function s = cellfun_value()
r = cellfun(@np, {1}); %#ok<NASGU>
s = logged_text();
end

function s = cellfun_two()
[r1, r2] = cellfun(@np2, {1}); %#ok<ASGLU>
s = logged_text();
end

function s = arrayfun_stmt()
arrayfun(@np, 1);
s = logged_text();
end

function s = arrayfun_value()
r = arrayfun(@np, 1); %#ok<NASGU>
s = logged_text();
end

function s = structfun_stmt()
structfun(@np, struct('a', 1));
s = logged_text();
end

function s = structfun_value()
r = structfun(@np, struct('a', 1)); %#ok<NASGU>
s = logged_text();
end

function s = method_stmt()
b = CallBox;
b.m();
s = logged_text();
end

function s = method_fn_stmt()
b = CallBox;
m(b);
s = logged_text();
end

function s = method_value()
b = CallBox;
y = b.m(); %#ok<NASGU>
s = logged_text();
end

function s = method_two()
b = CallBox;
[y1, y2] = b.m2(); %#ok<ASGLU>
s = logged_text();
end

function s = relay_stmt()
relay();
s = logged_text();
end

function s = relay_value()
v = relay(); %#ok<NASGU>
s = logged_text();
end

function s = relay_two()
[a, b] = relay(); %#ok<ASGLU>
s = logged_text();
end

function s = nested_stmt_and_value()
nn();
v = nn(); %#ok<NASGU>
s = logged_text();
    function y = nn()
        vlog(sprintf('nested%d', nargout));
        y = 1;
    end
end

function s = ctx_if()
if np()
end
s = logged_text();
end

function s = ctx_while()
while np()
end
s = logged_text();
end

function s = ctx_switch()
switch np()
    case 0
end
s = logged_text();
end

function s = ctx_for()
for k = np() %#ok<NASGU>
end
s = logged_text();
end

function s = ctx_bracket()
q = [np() np()]; %#ok<NASGU>
s = logged_text();
end

function s = ctx_brace()
q = {np()}; %#ok<NASGU>
s = logged_text();
end

function s = ctx_plus()
q = np() + 1; %#ok<NASGU>
s = logged_text();
end

function s = ctx_field()
t.f = np(); %#ok<STRNU>
s = logged_text();
end

function s = ctx_cellassign()
q{1} = np(); %#ok<NASGU>
s = logged_text();
end

function s = ctx_indexassign()
x = [1 2];
x(2) = np(); %#ok<NASGU>
s = logged_text();
end

function s = ctx_tilde()
[~] = np();
s = logged_text();
end

function s = ctx_tilde_two()
[~, ~] = np2();
s = logged_text();
end

function s = ctx_argument()
numel(np());
s = logged_text();
end

function s = eval_stmt()
eval('np();');
s = logged_text();
end

function s = evalin_stmt()
evalin('caller', 'np();');
s = logged_text();
end

function s = str2func_stmt()
h = str2func('np');
h();
s = logged_text();
end

function s = cellheld_stmt()
cc = {@np};
cc{1}();
s = logged_text();
end

function s = structheld_stmt()
t.f = @np;
t.f();
s = logged_text();
end

function s = builtin_feval_stmt()
builtin('feval', @np);
s = logged_text();
end

function s = builtin_feval_value()
v = builtin('feval', @np); %#ok<NASGU>
s = logged_text();
end

function s = nargout_query()
s = sprintf('%d %d %d %d %d', nargout('np'), nargout('np2'), nargout('np0'), nargout('vo_none'), nargout(@() np()));
end

% --- ans after a statement call ------------------------------------------------------------------------

function s = ans_two_stmt()
clear ans
two();
s = '0';
if exist('ans', 'var'), s = sprintf('1 %s', mat2str(ans)); end
end

function s = ans_vo_sets_first_stmt()
clear ans
vo_sets_first();
s = '0';
if exist('ans', 'var'), s = sprintf('1 %s', mat2str(ans)); end
end

function s = ans_named_unassigned_stmt()
clear ans
named_unassigned();
s = '0';
if exist('ans', 'var'), s = sprintf('1 %s', mat2str(ans)); end
end

function s = ans_vo_none_stmt()
clear ans
vo_none();
s = '0';
if exist('ans', 'var'), s = sprintf('1 %s', mat2str(ans)); end
end

function s = ans_second_only_stmt()
clear ans
second_only();
s = '0';
if exist('ans', 'var'), s = sprintf('1 %s', mat2str(ans)); end
end

function s = ans_method_two_stmt()
clear ans
b = CallBox;
b.m2();
s = '0';
if exist('ans', 'var'), s = sprintf('1 %s', mat2str(ans)); end
end

function s = ans_handle_np2_stmt()
clear ans
h = @np2;
h();
s = '0';
if exist('ans', 'var'), s = sprintf('1 %s', mat2str(ans)); end
end

function s = ans_anon_two_stmt()
clear ans
g = @() two();
g();
s = '0';
if exist('ans', 'var'), s = sprintf('1 %s', mat2str(ans)); end
end

function s = ans_feval_two_stmt()
clear ans
feval(@two);
s = '0';
if exist('ans', 'var'), s = sprintf('1 %s', mat2str(ans)); end
end

function s = ans_cellfun_stmt()
clear ans
cellfun(@np, {1});
s = '0';
if exist('ans', 'var'), s = sprintf('1 %s', mat2str(ans)); end
end

function s = ans_handle_vo_stmt()
clear ans
h = @vo_sets_first;
h();
s = '0';
if exist('ans', 'var'), s = sprintf('1 %s', mat2str(ans)); end
end

function s = ans_sin_handle_stmt()
clear ans
h = @sin;
h(1);
s = '0';
if exist('ans', 'var'), s = sprintf('1 %s', mat2str(ans)); end
end

function s = ans_feval_sin_stmt()
clear ans
feval(@sin, 0);
s = '0';
if exist('ans', 'var'), s = sprintf('1 %s', mat2str(ans)); end
end

function s = ans_size_handle_stmt()
clear ans
h = @size;
h(1);
s = '0';
if exist('ans', 'var'), s = sprintf('1 %s', mat2str(ans)); end
end

function s = ans_size_stmt()
clear ans
size(1);
s = '0';
if exist('ans', 'var'), s = sprintf('1 %s', mat2str(ans)); end
end

function s = ans_anon_expr_stmt()
clear ans
g = @() 42;
g();
s = '0';
if exist('ans', 'var'), s = sprintf('1 %s', mat2str(ans)); end
end

function s = ans_anon_sin_stmt()
clear ans
g = @() sin(1);
g();
s = '0';
if exist('ans', 'var'), s = sprintf('1 %s', mat2str(ans)); end
end

% --- the unassigned-output refusal (#84) --------------------------------------------------------------

function s = unassigned_named()
try
    x = named_unassigned(); %#ok<NASGU>
    s = 'no error';
catch e
    s = sprintf('%s %s', e.identifier, e.message);
end
end

function s = unassigned_vo_none()
try
    x = vo_none(); %#ok<NASGU>
    s = 'no error';
catch e
    s = sprintf('%s %s', e.identifier, e.message);
end
end

function s = unassigned_vo_short()
try
    [a, b] = vo_sets_first(); %#ok<ASGLU>
    s = 'no error';
catch e
    s = sprintf('%s %s', e.identifier, e.message);
end
end

function s = unassigned_nested()
try
    x = nun(); %#ok<NASGU>
    s = 'no error';
catch e
    s = sprintf('%s %s', e.identifier, e.message);
end
    function y = nun() %#ok<STOUT>
    end
end

function s = unassigned_method()
try
    b = CallBox;
    x = b.m_unassigned(); %#ok<NASGU>
    s = 'no error';
catch e
    s = sprintf('%s %s', e.identifier, e.message);
end
end

function s = unassigned_file()
try
    [~, b] = cc_second_only_file(); %#ok<NASGU>
    s = 'no error';
catch e
    s = sprintf('%s %s', e.identifier, e.message);
end
end

function s = unassigned_feval()
try
    [a, b] = feval(@second_only); %#ok<ASGLU>
    s = 'no error';
catch e
    s = sprintf('%s %s', e.identifier, e.message);
end
end

function s = unassigned_tilde()
try
    [~, b] = second_only(); %#ok<NASGU>
    s = 'no error';
catch e
    s = sprintf('%s %s', e.identifier, e.message);
end
end
