% record: -noFigureWindows
% Open item 336 (ADR 0378): eval binding a name a function's text reads as a function warns
% MATLAB:lang:ExternalShadowedInEval (a built-in, a file on the path) or LocalFunctionShadowedInEval (a local
% function) and the function's text goes on reading the function, bare or called; which names a built-in
% constant; feature('hotlinks') (true inside evalc in R2025b, whose capture carries links: ADR 0368's divergence).
global OI336ELIB
lib = tempname;
mkdir(lib);
OI336ELIB = lib;
put(fullfile(lib, 'oi336helper.m'), {'function r = oi336helper()', 'r = 42;', 'end'});
put(fullfile(lib, 'oi336shadow.m'), {
    'function r = oi336shadow(k)'
    'lastwarn('''');'
    'switch k'
    '    case 1'
    '        eval(''pi = 7;'');'
    '        v = pi;'
    '    case 2'
    '        eval(''sin = [5 6];'');'
    '        v = sin(0);'
    '    case 3'
    '        eval(''sin = [5 6];'');'
    '        v = sin(1);'
    '    case 4'
    '        eval(''max = [3 9];'');'
    '        v = max([1 2]);'
    '    case 5'
    '        eval(''oi336helper = 5;'');'
    '        v = oi336helper;'
    '    case 6'
    '        eval(''loc336 = 5;'');'
    '        v = loc336;'
    '    case 7'
    '        eval(''zz336 = 5;'');'
    '        v = zz336;'
    '    case 8'
    '        eval(''true = 8;'');'
    '        v = true(1);'
    '    case 9'
    '        eval(''[pi, q] = deal(7, 8);'');'
    '        v = pi;'
    '    case 10'
    '        eval(''for pi = 7:7, end'');'
    '        v = pi;'
    '    case 11'
    '        eval(''pi = 7;'');'
    '        v = [exist(''pi'', ''var''), eval(''pi''), pi];'
    '    case 12'
    '        evalc(''pi = 7;'');'
    '        v = pi;'
    '    case 13'
    '        eval(''pi = 7;'');'
    '        eval(''pi = 8;'');'
    '        v = pi;'
    '    case 14'
    '        eval(''eps = 7;'');'
    '        v = eval(''eps'');'
    '    case 15'
    '        h = @sin;'
    '        eval(''sin = 7;'');'
    '        v = h(0);'
    '    case 16'
    '        v = oi336binds();'
    '    case 17'
    '        eval(''linspace = 5;'');'
    '        try'
    '            v = linspace;'
    '        catch e'
    '            v = e.identifier;'
    '        end'
    'end'
    '[m, id] = lastwarn;'
    'if isnumeric(v) || islogical(v), v = mat2str(v); end'
    'r = sprintf(''%s # %s # %s # %s'', v, class(v), id, m);'
    'end'
    'function r = loc336()'
    'r = 77;'
    'end'
    });
put(fullfile(lib, 'oi336binds.m'), {'function v = oi336binds()', 'eval(''pi = 7;'');', 'v = pi;', 'if false', '    pi = 1;', 'end', 'end'});
addpath(lib);
names = {'pi', 'sin0', 'sin1', 'max', 'pathfile', 'localfn', 'undefined', 'true_call', 'multi', 'for', 'exist_eval', ...
    'evalc', 'twice', 'nomention', 'handle_only', 'binds', 'toolbox_file'};
for k = 1:numel(names)
    try
        chk(['shadow_' names{k}], oi336shadow(k));
    catch e
        chk(['shadow_' names{k}], ['ERR ' e.identifier ' ' e.message]);
    end
end
warning('error', 'MATLAB:lang:ExternalShadowedInEval');
try
    chk('shadow_error_state', oi336shadow(1));
catch e
    chk('shadow_error_state', ['ERR ' e.identifier ' ' e.message]);
end
warning('on', 'MATLAB:lang:ExternalShadowedInEval');
lastwarn('');
eval('pi = 7;');
[~, id] = lastwarn;
chk('shadow_script', sprintf('%g # %s', pi, id));
clear pi
rmpath(lib);
rmdir(lib, 's');

% --- which of a built-in constant ---------------------------------------------------------------
chk('which_pi_builtin', sprintf('%d', contains(which('pi'), 'built-in')));
chk('which_pi_printed', sprintf('%d', contains(evalc('which pi'), 'built-in')));
chk('which_eps_builtin', sprintf('%d', contains(which('eps'), 'built-in')));
chk('which_pi_text', which('pi'), 'div=ADR0378');

% --- feature('hotlinks') ----------------------------------------------------------------------
h = feature('hotlinks');
chk('hotlinks', sprintf('%s # %s', class(h), mat2str(h)));
h = feature('HotLinks');
chk('hotlinks_case', sprintf('%s # %s', class(h), mat2str(h)));
h = feature('hotlinks', 0);
chk('hotlinks_set', sprintf('%s # %s', class(h), mat2str(h)));
chk('hotlinks_in_evalc', evalc('h = feature(''hotlinks''); fprintf(''%d'', h)'), 'div=ADR0378');

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
