% record: -noFigureWindows
% Open item 350 (ADR 0385): getReport wraps a long message at the console's width (180 under -batch); eval,
% evalc and evalin head a name nothing answers in their text "Error using eval"; a function with nested
% functions is a static workspace eval, evalc, evalin and assignin cannot add a name to; pi, i and j take no
% input (MATLAB:maxrhs), and eps, realmax, realmin and flintmax answer R2025b's refusals.
% A report prints as its lines joined by ' / '.
lib = tempname;
mkdir(lib);
put(fullfile(lib, 'oi350nest.m'), {
    'function v = oi350nest(k)'
    'q = 1;'
    'shared = 3;'
    'lastwarn('''');'
    'switch k'
    '    case 1'
    '        eval(''pi = 7;'');'
    '        v = pi;'
    '    case 2'
    '        eval(''newv = 7;'');'
    '        v = 1;'
    '    case 3'
    '        eval(''q = 7;'');'
    '        v = q;'
    '    case 4'
    '        v = inner(1);'
    '    case 5'
    '        v = inner(2);'
    '    case 6'
    '        v = inner(3);'
    '    case 7'
    '        eval(''[q, newv2] = deal(4, 5);'');'
    '        v = q;'
    '    case 8'
    '        evalc(''newv3 = 7;'');'
    '        v = 1;'
    '    case 9'
    '        helper9();'
    '        v = 1;'
    '    case 10'
    '        helper10();'
    '        v = 1;'
    '    case 11'
    '        v = eval(''q + 1'');'
    '    case 12'
    '        eval(''q(3) = 9;'');'
    '        v = q;'
    '    case 13'
    '        eval(''for newv4 = 1:2, end'');'
    '        v = 1;'
    '    case 14'
    '        v = inner(4);'
    '    case 15'
    '        v = inner(5);'
    '    case 16'
    '        eval(''later = 7;'');'
    '        v = later;'
    '        later = 8; %#ok<NASGU>'
    '    case 17'
    '        eval(''reado = 7;'');'
    '        v = 1;'
    '        if false, disp(reado); end'
    '    case 18'
    '        v = loc350();'
    '    case 19'
    '        v = inner(6);'
    '    case 20'
    '        eval(''1 + 1;'');'
    '        v = ans; %#ok<NOANS>'
    '    case 21'
    '        global oi350g'
    '        eval(''oi350g = 4;'');'
    '        v = oi350g;'
    '    case 22'
    '        tmp = 1; %#ok<NASGU>'
    '        clear tmp'
    '        eval(''tmp = 5;'');'
    '        v = tmp;'
    '    case 23'
    '        f = @() eval(''anonv = 3;'');'
    '        f();'
    '        v = 1;'
    '    case 24'
    '        eval(''shared = 9; newv6 = 1;'');'
    '        v = shared;'
    'end'
    'v = sprintf(''%s # %s'', mat2str(v), lastwarn);'
    '    function r = inner(m)'
    '        w = 2;'
    '        switch m'
    '            case 1'
    '                eval(''pi = 7;'');'
    '                r = 1;'
    '            case 2'
    '                eval(''newin = 7;'');'
    '                r = 1;'
    '            case 3'
    '                eval(''w = 7;'');'
    '                r = w;'
    '            case 4'
    '                eval(''q = 8;'');'
    '                r = q;'
    '            case 5'
    '                eval(''shared = 8;'');'
    '                r = shared;'
    '            case 6'
    '                eval(''outerbound = 5;'');'
    '                r = outerbound;'
    '        end'
    '    end'
    'outerbound = 0; %#ok<NASGU>'
    'end'
    'function helper9()'
    'evalin(''caller'', ''newv5 = 3;'');'
    'end'
    'function helper10()'
    'assignin(''caller'', ''asgnew'', 7);'
    'end'
    'function v = loc350()'
    'eval(''locnew = 7;'');'
    'v = locnew;'
    'end'
    });
put(fullfile(lib, 'oi350plain.m'), {
    'function v = oi350plain(k)'
    'switch k'
    '    case 1'
    '        eval(''newv = 7;'');'
    '        v = newv;'
    '    case 2'
    '        f = @() eval(''anonv = 3;'');'
    '        f();'
    '        v = 1;'
    '    case 3'
    '        eval(''zzq350 + 1'');'
    '        v = 1;'
    '    case 4'
    '        eval(''error(''''oi350:e'''', ''''boom'''')'');'
    '        v = 1;'
    'end'
    'end'
    });
put(fullfile(lib, 'oi350undef.m'), {'function oi350undef()', 'zzq350inner + 1;', 'end'});
addpath(lib);

% --- a report's message wraps at the console's width ------------------------------------------
for L = [178 179 180 181]
    wrapcase(sprintf('wrap_%d', L), [repmat('a', 1, L - 4) ' bbb tail']);
end
wrapcase('wrap_exact_179', [repmat('a', 1, 175) ' bbb']);
wrapcase('wrap_exact_180', [repmat('a', 1, 176) ' bbb']);
wrapcase('wrap_one_long', repmat('z', 1, 200));
wrapcase('wrap_before_long', ['before ' repmat('z', 1, 200) ' after']);
wrapcase('wrap_double_space', [repmat('a', 1, 178) '  bb cc']);
wrapcase('wrap_space_at_180', [repmat('a', 1, 180) ' bb']);
wrapcase('wrap_tab', [repmat('a', 1, 175) sprintf('\t') 'bbbbbbbbbb cc']);
wrapcase('wrap_leading_space', ['   ' strtrim(repmat('abcd ', 1, 50))]);
wrapcase('wrap_trailing_space', [strtrim(repmat('abcd ', 1, 36)) '      ']);
wrapcase('wrap_hyphen', [repmat('a', 1, 175) '-bbbbbbbbbb cc']);
wrapcase('wrap_cr', [repmat('a', 1, 100) char(13) repmat('b', 1, 100) ' cc']);
wrapcase('wrap_two_lines', [strtrim(repmat('abcd ', 1, 50)) newline strtrim(repmat('efgh ', 1, 50))]);
wrapcase('wrap_three', strtrim(repmat('abcdefghi ', 1, 60)));
try
    error('oi350:w', '%s', [repmat(char(233), 1, 178) ' bbb tail']);
catch e
    chk('wrap_accented_lines', mat2str(cellfun(@numel, strsplit(getReport(e), newline))));
end
wrapcase('wrap_short', 'a short message');
try
    error(['Prefix ' strtrim(repmat('word ', 1, 40))]);
catch e
    rep('wrap_noid', e);
    rep('wrap_basic', e, 'basic');
end
try
    me = MException('oi350:top', '%s', strtrim(repmat('top ', 1, 50)));
    me = addCause(me, MException('oi350:cause', '%s', strtrim(repmat('cause ', 1, 40))));
    throw(me);
catch e
    rep('wrap_cause', e);
end
try
    eval([repmat('z', 1, 160) '350 + 1;']);
catch e
    rep('wrap_undefined_name', e);
end
try
    error('oi350:w', '%s', strtrim(repmat('word ', 1, 40)));
catch e
    chk('wrap_message_unwrapped', sprintf('%d', numel(e.message)));
end

% --- eval's heading for a name nothing answers --------------------------------------------------
cases = {
    'undef', 'eval(''zzq350'')'
    'undef_expr', 'eval(''zzq350 + 1'')'
    'undef_call', 'eval(''zzq350(3)'')'
    'undef_assign', 'eval(''a350 = zzq350;'')'
    'undef_out', 'v350 = eval(''zzq350'');'
    'undef_two_stmts', 'eval(''b350 = 1; zzq350'')'
    'undef_nested_eval', 'eval(''eval(''''zzq350'''')'')'
    'undef_evalc', 'x350 = evalc(''zzq350'');'
    'undef_evalin', 'evalin(''base'', ''zzq350'')'
    'undef_catch_text', 'eval(''zzq350'', ''zzq351'')'
    'undef_caught', 'eval(''zzq350'', ''error(''''oi350:c'''', ''''caught'''')'')'
    'undef_in_called_fn', 'eval(''oi350undef()'')'
    'undef_handle', 'eval(''h350 = @zzq350; h350(1)'')'
    'index_in_eval', 'eval(''y350 = [1 2]; y350(5)'')'
    'error_in_eval', 'eval(''error(''''oi350:e'''', ''''boom'''')'')'
    'field_in_eval', 'eval(''s350 = struct(''''a'''', 1); s350.zz'')'
    'dotted_in_eval', 'eval(''zzq350.b.c'')'
    };
for k = 1:size(cases, 1)
    try
        eval(cases{k, 2});
        chk(cases{k, 1}, 'no error');
    catch e
        rule = 'exact';
        if strcmp(cases{k, 1}, 'undef_handle')
            rule = 'div=ADR0385';
        end
        rep(cases{k, 1}, e, [], rule);
    end
end
for k = 3:4
    try
        oi350plain(k);
    catch e
        rep(sprintf('in_function_%d', k), e);
    end
end

% --- the static workspace of a function with nested functions --------------------------------
names = {'pi_outer', 'new_outer', 'existing_outer', 'pi_nested', 'new_nested', 'existing_nested', 'multi', 'evalc', ...
    'evalin_caller', 'assignin_caller', 'eval_read', 'index_assign', 'for_loop', 'outer_var_from_nested', 'shared_var', ...
    'bound_later', 'read_only', 'local_function', 'outer_binds_later', 'ans', 'global', 'cleared', 'anonymous', 'two_names'};
for k = 1:numel(names)
    try
        chk(['static_' names{k}], oi350nest(k));
    catch e
        rep(['static_' names{k}], e);
    end
end
for k = 1:2
    try
        chk(sprintf('plain_%d', k), mat2str(oi350plain(k)));
    catch e
        rep(sprintf('plain_%d', k), e);
    end
end
rmpath(lib);
rmdir(lib, 's');

% --- pi, i and j take no input; eps, realmax, realmin and flintmax --------------------------------
ex = {'pi(1)', 'pi(2)', 'pi(1,2)', 'pi([])', 'pi(''single'')', 'pi()', 'i(1)', 'j(2)', 'i()', 'j()', ...
    'eps(''single'')', 'eps(''double'')', 'eps(1,2)', 'eps(''like'', 1)', 'eps(''like'', single(1))', 'eps(''Like'', 1)', ...
    'realmax(1)', 'realmin(''int8'')', 'flintmax(2)', 'realmax(''single'')', 'Inf(1)', 'NaN(1,2)'};
for k = 1:numel(ex)
    try
        v = eval(ex{k});
        chk(['const_' regexprep(ex{k}, '\W', '_')], sprintf('%s %s %s', class(v), mat2str(size(v)), mat2str(v, 6)));
    catch e
        chk(['const_' regexprep(ex{k}, '\W', '_')], ['ERR ' e.identifier ' ' e.message]);
    end
end
try
    pi(1)
catch e
    rep('const_report', e);
end
chk('const_variable', localpi());

function r = localpi()
pi = [5 6];
r = mat2str(pi(2));
end

function wrapcase(key, m)
try
    error('oi350:w', '%s', m);
catch e
    rep(key, e);
end
end

function rep(key, e, form, rule)
if nargin < 3 || isempty(form)
    r = getReport(e);
else
    r = getReport(e, form);
end
if nargin < 4
    rule = 'exact';
end
r = strrep(r, sprintf('\t'), '<TAB>');
r = strrep(r, char(13), '<CR>');
chk(key, sprintf('%s # %s', e.identifier, strjoin(strsplit(r, newline), ' / ')), rule);
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
