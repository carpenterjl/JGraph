% output_count_checks.m -- V9.3 of the value-ownership plan (ADR 0170): a call asked for more
% outputs than the callee has is refused (#55). A user function - local, nested, a method, through
% a handle, feval, an anonymous body, cellfun/arrayfun, eval, a varargout relay - refuses as
% MATLAB:TooManyOutputs; a builtin (disp, error, clc, save, clear, assignin, rethrow, drawnow, max
% asked for three) as MATLAB:maxlhs, and a MATLAB function that is a file (hold) as
% MATLAB:TooManyOutputs. When: a direct call of a user function refuses before its arguments run
% (bump does not run); a builtin, a handle, feval and an anonymous forward refuse after them (bump
% ran), while the anonymous body's own direct call refuses before its own arguments. A varargout
% function asked for more than it set is the unassigned-output refusal, not this one. Two
% builtins that nargout reports with no output still accept one (pause, and beep - measured);
% warning, rng, fclose and fprintf answer as before. A case's value is the identifier, and the
% log of which arguments ran.

run_case('x_none_out', @x_none_out);
run_case('ab_one_out', @ab_one_out);
run_case('x_none_out_arg_bump', @x_none_out_arg_bump);
run_case('x_disp_bump', @x_disp_bump);
run_case('ab_one_out_arg_bump', @ab_one_out_arg_bump);
run_case('anon_none_bump', @anon_none_bump);
run_case('anon_disp_bump', @anon_disp_bump);
run_case('anon_body_bump_inside', @anon_body_bump_inside);
run_case('handle_none_bump', @handle_none_bump);
run_case('feval_none_bump', @feval_none_bump);
run_case('feval_disp_bump', @feval_disp_bump);
run_case('abc_max', @abc_max);
run_case('ab_size', @ab_size);
run_case('r_cellfun_none', @r_cellfun_none);
run_case('cellfun_none_stmt', @cellfun_none_stmt);
run_case('cellfun_anon_error_stmt', @cellfun_anon_error_stmt);
run_case('r_cellfun_anon_error', @r_cellfun_anon_error);
run_case('r_arrayfun_none', @r_arrayfun_none);
run_case('x_builtin_disp', @x_builtin_disp);
run_case('x_method_noout', @x_method_noout);
run_case('ab_method_one', @ab_method_one);
run_case('x_anon_none', @x_anon_none);
run_case('ab_anon_one', @ab_anon_one);
run_case('ab_anon_identity', @ab_anon_identity);
run_case('x_handle_none', @x_handle_none);
run_case('x_eval_none', @x_eval_none);
run_case('ab_relay_one', @ab_relay_one);
run_case('x_bare_none', @x_bare_none);
run_case('x_structheld_none', @x_structheld_none);
run_case('x_nested_none', @x_nested_none);
run_case('ab_feval_one', @ab_feval_one);
run_case('ab_handle_one', @ab_handle_one);
run_case('ab_cellfun_one_nonuniform', @ab_cellfun_one_nonuniform);
run_case('abc_vo_two', @abc_vo_two);
run_case('x_handle_disp_bump', @x_handle_disp_bump);
run_case('x_error_bump', @x_error_bump);
run_case('x_fprintf', @x_fprintf);
run_case('x_clc', @x_clc);
run_case('x_error', @x_error);
run_case('x_disp', @x_disp);
run_case('x_hold', @x_hold);
run_case('x_drawnow', @x_drawnow);
run_case('x_assignin', @x_assignin);
run_case('x_rethrow', @x_rethrow);
run_case('x_clear', @x_clear);
run_case('x_save', @x_save);
run_case('x_pause0', @x_pause0);
run_case('x_warning', @x_warning);
run_case('x_rng', @x_rng);
run_case('x_fclose_all', @x_fclose_all);

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

function s = verdict(e)
s = sprintf('%s log=%s', e.identifier, logged_text());
end

% --- the callees ------------------------------------------------------------------------------------

function none_out()
vlog('ran');
end

function none_out_arg(v) %#ok<INUSD>
vlog('ran');
end

function y = one_out()
vlog('ran');
y = 1;
end

function y = one_out_arg(v)
vlog('ran');
y = v;
end

function y = bump()
vlog('bump');
y = 1;
end

function varargout = vo_two()
varargout{1} = 1;
varargout{2} = 2;
end

function varargout = relay_one()
[varargout{1:nargout}] = one_out();
end

% --- the cases --------------------------------------------------------------------------------------

function s = x_none_out()
try
    x = none_out(); %#ok<NASGU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = ab_one_out()
try
    [a, b] = one_out(); %#ok<ASGLU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = x_none_out_arg_bump()
try
    x = none_out_arg(bump()); %#ok<NASGU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = x_disp_bump()
try
    x = disp(bump()); %#ok<NASGU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = ab_one_out_arg_bump()
try
    [a, b] = one_out_arg(bump()); %#ok<ASGLU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = anon_none_bump()
try
    f = @(v) none_out_arg(v);
    x = f(bump()); %#ok<NASGU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = anon_disp_bump()
try
    f = @(v) disp(v);
    x = f(bump()); %#ok<NASGU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = anon_body_bump_inside()
try
    f = @(v) none_out_arg(bump());
    x = f(1); %#ok<NASGU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = handle_none_bump()
try
    h = @none_out_arg;
    x = h(bump()); %#ok<NASGU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = feval_none_bump()
try
    x = feval(@none_out_arg, bump()); %#ok<NASGU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = feval_disp_bump()
try
    x = feval('disp', bump()); %#ok<NASGU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = abc_max()
try
    [a, b, q] = max([1 2]); %#ok<ASGLU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = ab_size()
[a, b] = size(1);
s = sprintf('ok %d %d', a, b);
end

function s = r_cellfun_none()
try
    r = cellfun(@none_out_arg, {1}); %#ok<NASGU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = cellfun_none_stmt()
cellfun(@none_out_arg, {1});
s = sprintf('ok log=%s', logged_text());
end

function s = cellfun_anon_error_stmt()
try
    cellfun(@(x) error('a:b', 'c'), {1});
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = r_cellfun_anon_error()
try
    r = cellfun(@(x) error('a:b', 'c'), {1}); %#ok<NASGU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = r_arrayfun_none()
try
    r = arrayfun(@none_out_arg, 1); %#ok<NASGU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = x_builtin_disp()
try
    x = builtin('disp', 1); %#ok<NASGU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = x_method_noout()
try
    b = CallBox;
    x = b.noout(); %#ok<NASGU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = ab_method_one()
try
    b = CallBox;
    [a, q] = b.m(); %#ok<ASGLU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = x_anon_none()
try
    g = @() none_out();
    x = g(); %#ok<NASGU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = ab_anon_one()
try
    g1 = @() one_out();
    [a, b] = g1(); %#ok<ASGLU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = ab_anon_identity()
try
    f = @(x) x;
    [a, b] = f(1); %#ok<ASGLU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = x_handle_none()
try
    h = @none_out;
    x = h(); %#ok<NASGU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = x_eval_none()
try
    x = eval('none_out()'); %#ok<NASGU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = ab_relay_one()
try
    [a, b] = relay_one(); %#ok<ASGLU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = x_bare_none()
try
    x = none_out; %#ok<NASGU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = x_structheld_none()
try
    t.f = @none_out;
    x = t.f(); %#ok<NASGU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = x_nested_none()
try
    x = nnone(); %#ok<NASGU>
    s = 'no error';
catch e
    s = verdict(e);
end
    function nnone()
        vlog('ran');
    end
end

function s = ab_feval_one()
try
    [a, b] = feval(@one_out); %#ok<ASGLU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = ab_handle_one()
try
    h = @one_out;
    [a, b] = h(); %#ok<ASGLU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = ab_cellfun_one_nonuniform()
try
    [a, b] = cellfun(@one_out_arg, {1}, 'UniformOutput', false); %#ok<ASGLU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = abc_vo_two()
try
    [a, b, q] = vo_two(); %#ok<ASGLU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = x_handle_disp_bump()
try
    h = @disp;
    x = h(bump()); %#ok<NASGU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = x_error_bump()
try
    x = error('a:b', '%d', bump()); %#ok<NASGU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = x_fprintf()
x = fprintf('');
s = sprintf('ok %d', x);
end

function s = x_clc()
try
    x = clc; %#ok<NASGU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = x_error()
try
    x = error('a:b', 'c'); %#ok<NASGU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = x_disp()
try
    x = disp(1); %#ok<NASGU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = x_hold()
try
    x = hold('on'); %#ok<NASGU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = x_drawnow()
try
    x = drawnow; %#ok<NASGU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = x_assignin()
try
    x = assignin('caller', 'zzq', 1); %#ok<NASGU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = x_rethrow()
try
    x = rethrow(MException('a:b', 'c')); %#ok<NASGU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = x_clear()
try
    zzq = 1; %#ok<NASGU>
    x = clear('zzq'); %#ok<NASGU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = x_save()
try
    x = save(tempname); %#ok<NASGU>
    s = 'no error';
catch e
    s = verdict(e);
end
end

function s = x_pause0()
try
    x = pause(0); %#ok<NASGU>
    s = 'ok';
catch e
    s = verdict(e);
end
end

function s = x_warning()
x = warning('off', 'a:b');
s = sprintf('ok %s', class(x));
end

function s = x_rng()
x = rng;
s = sprintf('ok %s', class(x));
end

function s = x_fclose_all()
x = fclose('all');
s = sprintf('ok %d', x);
end
