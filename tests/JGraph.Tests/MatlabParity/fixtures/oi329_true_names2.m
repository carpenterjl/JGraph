% record: -noFigureWindows
% Open item 329 (ADR 0368): the leftovers of ADR 0363's true and false as names - iskeyword, which naming
% a variable, the MATLAB:lang:ExternalShadowedInEval warning an eval binding the word gives in a function
% that reads the built-in, eval('true') asked for a value answering the built-in past a true.m on the
% path, and a nested function named true answering only the mentions in the function it is nested in.
% A function read before the true.m appeared keeps the built-in in R2025b (ADR 0363's divergence), so no
% helper below mentions either word bare.

% --- iskeyword ----------------------------------------------------------------------------------
k = iskeyword;
chk('kw_list', sprintf('%s %s %s', class(k), mat2str(size(k)), strjoin(k', ',')));
cases = {'iskeyword(''for'')', 'iskeyword(''true'')', 'iskeyword("for")', 'iskeyword({''for''})', ...
    'iskeyword({''for'',''x''})', 'iskeyword(["for" "x"])', 'iskeyword(5)', 'iskeyword([])', ...
    'iskeyword(''For'')', 'iskeyword('' for'')', 'iskeyword(''for '')', 'iskeyword(''for'', 1)', 'iskeyword({})', ...
    'iskeyword(''end'')', 'iskeyword(''parfor'')', 'iskeyword(''spmd'')', 'iskeyword(''arguments'')', ...
    'iskeyword(''methods'')', 'iskeyword(''properties'')', 'iskeyword(''enumeration'')', ...
    'iskeyword({''for''; ''if''})', 'iskeyword(string.empty)', 'iskeyword(char([102 111 114]))', ...
    'iskeyword(categorical({''for''}))', 'iskeyword(iskeyword)', 'iskeyword({5})', 'iskeyword({"for"})', ...
    'iskeyword(['''' ''for''; ''if ''])', 'iskeyword(string(missing))'};
for q = 1:numel(cases)
    try
        v = eval(cases{q});
        chk(['kw ' cases{q}], sprintf('%s %s %d', class(v), mat2str(size(v)), v));
    catch err
        chk(['kw ' cases{q}], [err.identifier ' / ' err.message]);
    end
end
iskeyword('if');
chk('kw_ans', sprintf('%s %d', class(ans), ans));
chk('kw_nargout', tryit('[a, b] = iskeyword(''if'');'));

% --- which names a variable ---------------------------------------------------------------------
chk('which_builtin', sprintf('%d', contains(which('true'), 'built-in')));
true = 3;
chk('which_var_printed', strtrim(evalc('which true')));
chk('which_var_value', which('true'));
chk('which_var_exist', sprintf('%d', exist('true')));
clear true
chk('which_cleared', sprintf('%d', contains(which('true'), 'built-in')));
zz329 = 1;
chk('which_other_var', strtrim(evalc('which zz329')));
chk('which_other_value', which('zz329'));
max = 2;
chk('which_shadowing_var', strtrim(evalc('which max')));
clear max
chk('which_in_fn_bound', whichinfnbound());
chk('which_in_fn_free', whichinfnfree());

% --- the warning an eval binding the word gives -------------------------------------------------
[a1, a2, a3] = evalbind();
chk('warn_eval_true', a1);
chk('warn_eval_true_read', a2);
chk('warn_eval_true_evalread', a3);
[a1, a2] = evalbindfalse();
chk('warn_eval_false', a1);
chk('warn_eval_false_read', a2);
[a1, a2] = evalbindlater();
chk('warn_eval_after', a1);
chk('warn_eval_after_read', a2);
chk('warn_evalc', evalcbind());
chk('warn_bound_fn', evalboundfn());
[a1, a2] = evalcallform();
chk('warn_call_form', a1);
chk('call_form_read', a2);
chk('warn_unmentioned', evalunmentioned());
chk('warn_assignin', assigninbind());
chk('warn_evalin_caller', evalincaller());
chk('warn_multi', evalmulti());
lastwarn('');
eval('true = 7;');
[m, id] = lastwarn;
chk('warn_script', [id ' / ' m]);
chk('warn_script_read', sprintf('%d', true));
clear true

% --- a nested function named true ----------------------------------------------------------------
lib = tempname;
mkdir(lib);
put(fullfile(lib, 'oi329n.m'), {'function r = oi329n()', 'r = {outer(), sib(), true, sib2(), class(true(2))};', 'end', ...
    'function r = outer()', 'r = {true, inner()};', '    function y = true()', '        y = ''nested'';', '    end', ...
    '    function y = inner()', '        y = true;', '    end', 'end', ...
    'function r = sib()', 'r = true;', 'end', 'function r = sib2()', 'r = true(1, 2);', 'end'});
addpath(lib);
r = oi329n();
chk('nested_outer', describe(r{1}{1}));
chk('nested_sibling_nested', describe(r{1}{2}));
chk('nested_sib', describe(r{2}));
chk('nested_main', describe(r{3}));
chk('nested_sib_call', describe(r{4}));
chk('nested_main_call', r{5});

% --- eval('true') with a true.m on the path ------------------------------------------------------
put(fullfile(lib, 'true.m'), {'function r = true(varargin)', 'r = ''file'';', 'end'});
put(fullfile(lib, 'false.m'), {'function r = false(varargin)', 'r = ''ffile'';', 'end'});
rehash;
x = eval('true');
chk('file_eval_out', describe(x));
eval('true');
chk('file_eval_noout', describe(ans));
x = eval('false');
chk('file_eval_false', describe(x));
x = eval(' true ');
chk('file_eval_spaced', describe(x));
x = eval('true;');
chk('file_eval_semi', describe(x));
x = eval('true,');
chk('file_eval_comma', describe(x));
x = eval('(true)');
chk('file_eval_parens', describe(x));
x = eval('true()');
chk('file_eval_call', describe(x));
x = eval('true(1)');
chk('file_eval_arg', describe(x));
x = eval('[true]');
chk('file_eval_bracket', describe(x));
x = eval("true");
chk('file_eval_string', describe(x));
eval('y = true;');
chk('file_eval_stmt', describe(y));
x = evalin('base', 'true');
chk('file_evalin_base', describe(x));
evalin('base', 'yb = true;');
chk('file_evalin_stmt', describe(yb));
x = eval('true', 'false');
chk('file_eval_catch', describe(x));
t = evalc('true');
chk('file_evalc', describe(ans));
y = true;
chk('file_script', describe(y));
[r1, r2, r3, r5] = fninside();
chk('file_fn_eval', describe(r1));
chk('file_fn_evalin_caller', describe(r2));
chk('file_fn_evalin_base', describe(r3));
chk('file_fn_stmt', describe(r5));
g = @() eval('true');
chk('file_anon_eval', describe(g()));
c = cell(1, 1);
[c{1}] = eval('true');
chk('file_eval_cellout', describe(c{1}));
c = {eval('true')};
chk('file_eval_in_cell', describe(c{1}));
true = 5;
x = eval('true');
chk('file_eval_var', describe(x));
clear true
chk('file_eval_bound_fn', describe(boundeval()));
chk('which_with_file', strrep(which('true'), lib, '<LIB>'));
delete(fullfile(lib, 'true.m'));
delete(fullfile(lib, 'false.m'));
rehash;
rmpath(lib);
rmdir(lib, 's');

function r = whichinfnbound()
true = 1; %#ok<NASGU>
r = strtrim(evalc('which true'));
end

function r = whichinfnfree()
eval('zzf = 1;');
r = strtrim(evalc('which zzf'));
end

function [a, b, c] = evalbind()
lastwarn('');
eval('true = 7;');
[m, id] = lastwarn;
a = [id ' / ' m];
b = class(true);
c = class(eval('true'));
end

function [a, b] = evalbindfalse()
lastwarn('');
eval('false = 7;');
[m, id] = lastwarn;
a = [id ' / ' m];
b = class(false);
end

function [a, b] = evalbindlater()
b = class(true);
lastwarn('');
eval('true = 7;');
[m, id] = lastwarn;
a = [id ' / ' m];
end

function r = evalcbind()
lastwarn('');
evalc('true = 8;');
[m, id] = lastwarn;
r = [id ' / ' m ' # ' class(true)];
end

function r = evalboundfn()
true = 1; %#ok<NASGU>
lastwarn('');
eval('true = 8;');
[m, id] = lastwarn;
r = [id ' / ' m ' # ' num2str(true)];
end

function [a, b] = evalcallform()
lastwarn('');
eval('true = 8;');
[m, id] = lastwarn;
a = [id ' / ' m];
b = class(true(1));
end

function r = evalunmentioned()
lastwarn('');
eval('false = 8;');
[m, id] = lastwarn;
r = [id ' / ' m ' # ' num2str(eval('false'))];
end

function r = assigninbind()
lastwarn('');
setter();
[m, id] = lastwarn;
r = [id ' / ' m ' # ' class(true)];
end

function setter()
assignin('caller', 'true', 9);
end

function r = evalincaller()
lastwarn('');
setter2();
[m, id] = lastwarn;
r = [id ' / ' m ' # ' class(true)];
end

function setter2()
evalin('caller', 'true = 9;');
end

function r = evalmulti()
lastwarn('');
eval('[q, true] = deal(1, 2);');
[m, id] = lastwarn;
r = [id ' / ' m ' # ' class(true)];
end

function [r1, r2, r3, r5] = fninside()
r1 = eval('true');
r2 = evalin('caller', 'true');
r3 = evalin('base', 'true');
eval('r5 = true;');
end

function r = boundeval()
true = 4; %#ok<NASGU>
r = eval('true');
end

function s = describe(v)
if ischar(v)
    s = ['char ''' v ''''];
else
    s = sprintf('%s %s %s', class(v), mat2str(size(v)), mat2str(v));
end
end

function s = tryit(code)
try
    eval(code);
    s = 'ok';
catch err
    s = [err.identifier ' / ' err.message];
end
end

function put(path, lines)
fid = fopen(path, 'w');
for q = 1:numel(lines)
    fprintf(fid, '%s\n', lines{q});
end
fclose(fid);
end

function chk(name, v, rule)
if nargin < 3
    rule = 'exact';
end
fprintf('CHK|%s|%s|%s\n', name, v, rule);
end
