% inputname_roads.m -- V9.2 of the value-ownership plan (ADR 0170): the call site is handed on to
% the frame on every road that has argument syntax - a named call, a multiple assignment, a
% handle (statement, value and multiple-output forms, #78), feval by handle and by name (#79), an
% anonymous body (whose own call is the site: a parameter's name, a captured name, nothing for an
% expression), a method by dot and by name (the receiver is argument one), a nested function, a
% nested function's handle, eval, str2func, a handle held in a cell or a struct - and cleared for a
% callback a builtin makes with no syntax (cellfun, arrayfun, structfun, an ErrorHandler), where
% R2025b answers ''. An argument written as anything but a plain name answers ''; an index past
% the arguments answers ''; index zero is refused; a script is refused. A case's value is the log
% of what inputname answered.

run_case('named', @named);
run_case('named_two', @named_two);
run_case('multi_named', @multi_named);
run_case('handle_stmt', @handle_stmt);
run_case('handle_value', @handle_value);
run_case('handle_multi', @handle_multi);
run_case('feval_handle', @feval_handle);
run_case('feval_name', @feval_name);
run_case('feval_value', @feval_value);
run_case('feval_multi', @feval_multi);
run_case('feval_expr', @feval_expr);
run_case('anon_param', @anon_param);
run_case('anon_captured', @anon_captured);
run_case('anon_expr', @anon_expr);
run_case('cellfun_callback', @cellfun_callback);
run_case('cellfun_nonuniform_callback', @cellfun_nonuniform_callback);
run_case('arrayfun_callback', @arrayfun_callback);
run_case('structfun_callback', @structfun_callback);
run_case('cellfun_anon_param', @cellfun_anon_param);
run_case('errorhandler_callback', @errorhandler_callback);
run_case('expr_arg', @expr_arg);
run_case('index_arg', @index_arg);
run_case('literal_arg', @literal_arg);
run_case('beyond_nargin', @beyond_nargin);
run_case('zero_index', @zero_index);
run_case('method_dot', @method_dot);
run_case('method_fn', @method_fn);
run_case('nested', @nested);
run_case('nested_handle', @nested_handle);
run_case('eval_stmt', @eval_stmt);
run_case('str2func_handle', @str2func_handle);
run_case('cellheld', @cellheld);
run_case('structheld', @structheld);
run_case('script_level', @script_level);

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

function v = in1(a)
vlog(['[' inputname(1) ']']);
v = a;
end

function v = in2(a, b) %#ok<INUSD>
vlog(sprintf('[%s][%s]', inputname(1), inputname(2)));
v = a;
end

function in_k(a, b, k) %#ok<INUSL>
vlog(['[' inputname(k) ']']);
end

function [a, b] = two_named(v)
a = inputname(1);
b = v;
vlog(['[' a ']']);
end

% --- the roads --------------------------------------------------------------------------------------

function s = named()
x = 5;
in1(x);
s = logged_text();
end

function s = named_two()
x = 5; y = 7;
in2(x, y);
s = logged_text();
end

function s = multi_named()
x = 5;
[a, b] = two_named(x); %#ok<ASGLU>
s = logged_text();
end

function s = handle_stmt()
x = 5;
h = @in1;
h(x);
s = logged_text();
end

function s = handle_value()
x = 5;
h = @in1;
v = h(x); %#ok<NASGU>
s = logged_text();
end

function s = handle_multi()
x = 5;
h = @two_named;
[a, b] = h(x); %#ok<ASGLU>
s = logged_text();
end

function s = feval_handle()
x = 5;
feval(@in1, x);
s = logged_text();
end

function s = feval_name()
x = 5;
feval('in1', x);
s = logged_text();
end

function s = feval_value()
x = 5;
v = feval(@in1, x); %#ok<NASGU>
s = logged_text();
end

function s = feval_multi()
x = 5;
[a, b] = feval(@two_named, x); %#ok<ASGLU>
s = logged_text();
end

function s = feval_expr()
x = 5;
feval(@in1, x + 1);
s = logged_text();
end

function s = anon_param()
x = 5;
g = @(q) in1(q);
g(x);
s = logged_text();
end

function s = anon_captured()
x = 5;
g = @(q) in1(x); %#ok<INUSD>
g(1);
s = logged_text();
end

function s = anon_expr()
x = 5;
g = @(q) in1(q + 1);
g(x);
s = logged_text();
end

function s = cellfun_callback()
x = 5;
cellfun(@in1, {x});
s = logged_text();
end

function s = cellfun_nonuniform_callback()
x = 5;
cellfun(@in1, {x}, 'UniformOutput', false);
s = logged_text();
end

function s = arrayfun_callback()
x = 5;
arrayfun(@in1, x);
s = logged_text();
end

function s = structfun_callback()
x = 5;
structfun(@in1, struct('a', x));
s = logged_text();
end

function s = cellfun_anon_param()
x = 5;
cellfun(@(q) in1(q), {x});
s = logged_text();
end

function s = errorhandler_callback()
x = 5;
cellfun(@(q) error('a:b', 'c'), {x}, 'ErrorHandler', @in2);
s = logged_text();
end

function s = expr_arg()
x = 5;
in1(x + 1);
s = logged_text();
end

function s = index_arg()
x = [5 6];
in1(x(1));
s = logged_text();
end

function s = literal_arg()
in1(5);
s = logged_text();
end

function s = beyond_nargin()
x = 5; y = 7;
in_k(x, y, 3);
s = logged_text();
end

function s = zero_index()
x = 5; y = 7;
in_k(x, y, 0);
s = logged_text();
end

function s = method_dot()
x = 5;
b = CallBox;
b.inp(x);
s = logged_text();
end

function s = method_fn()
x = 5;
b = CallBox;
inp(b, x);
s = logged_text();
end

function s = nested()
x = 5;
nin(x);
s = logged_text();
    function nin(q) %#ok<INUSD>
        vlog(['[' inputname(1) ']']);
    end
end

function s = nested_handle()
x = 5;
h = @nin;
h(x);
s = logged_text();
    function nin(q) %#ok<INUSD>
        vlog(['[' inputname(1) ']']);
    end
end

function s = eval_stmt()
x = 5; %#ok<NASGU>
eval('in1(x);');
s = logged_text();
end

function s = str2func_handle()
x = 5;
h = str2func('in1');
h(x);
s = logged_text();
end

function s = cellheld()
x = 5;
cc = {@in1};
cc{1}(x);
s = logged_text();
end

function s = structheld()
x = 5;
t.f = @in1;
t.f(x);
s = logged_text();
end

function s = script_level()
x = 5; %#ok<NASGU>
cc_inputname_script
s = logged_text();
end
