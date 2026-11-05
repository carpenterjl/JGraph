% w10_analyzer - what R2025b's Code Analyzer says over small files written to raise the first set of
% checks (App Designer window plan, stage W10, decision WO5): each message's id, line, columns, text
% and whether a fix is offered, through checkcode -id -struct; the display forms of checkcode and
% mlint (plain, -id, -fullpath, -string, -cyc, -modcyc, -notok, a cell of files, two outputs, -text);
% the refusals; codeIssues' object display, its Issues table and SuppressedIssues; and the
% suppression comments (%#ok, %#ok<ID>, %#ok<*ID>) with MSNU. Headless.
%   powershell -ExecutionPolicy Bypass -File .\run-probe.ps1 -Name w10_analyzer
here = fileparts(mfilename('fullpath'));
work = fullfile(here, 'rt', 'analyzer');
if exist(work, 'dir'), rmdir(work, 's'); end
mkdir(work);
accept = fullfile(fileparts(here), 'ds8', 'rt', 'accept');
if exist(fullfile(accept, 'AcceptForm.mlapp'), 'file')
    copyfile(fullfile(accept, 'AcceptForm.mlapp'), work);
    copyfile(fullfile(accept, 'AcceptFormPlain.m'), work);
end
short = @(s) strrep(strrep(char(s), work, '<work>'), strrep(work, '\', '/'), '<work>');

% --- the files --------------------------------------------------------------------------------------
writeFile(fullfile(work, 'a1_unused.m'), { ...
    'function [out1, out2, out3] = a1_unused(a, b, c, d)', ...   % 1  b unused (INUSD)
    '% the unused family', ...                                     % 2
    'x = 1;', ...                                                  % 3  NASGU
    '[p, q] = size(a);', ...                                       % 4  ASGLU p
    'out1 = q + c;', ...                                           % 5
    's.f = 2;', ...                                                % 6  STRNU
    't = struct();', ...                                           % 7
    't.g = 3;', ...                                                % 8  STRNU?
    'out2 = helper1(d);', ...                                      % 9
    'disp(out2)', ...                                              % 10
    'y = 1; %#ok<NASGU>', ...                                      % 11 suppressed
    'z = 1; %#ok', ...                                             % 12 suppressed bare
    'w = 1; %#ok<INUSD>', ...                                      % 13 NASGU + MSNU
    'out3 = helper2(1);', ...                                      % 14 LTARG
    'u1 = 1;', ...                                                 % 15 NASGU? (overwritten)
    'u1 = 2;', ...                                                 % 16
    'disp(u1)', ...                                                % 17
    'v1(2) = 3;', ...                                              % 18 NASGU?
    'helper6(3);', ...                                             % 19
    'helper7(gca);', ...                                           % 20
    'helper4();', ...                                              % 21
    'helper5(1);', ...                                             % 22
    'end', ...                                                     % 23
    '', ...                                                        % 24
    'function r = helper1(v)', ...                                 % 25
    'global G1', ...                                               % 26 NUSED
    'global G2', ...                                               % 27
    'persistent P1', ...                                           % 28
    'r = v + G2 + P1;', ...                                        % 29 PSET?
    'end', ...                                                     % 30
    '', ...                                                        % 31
    'function r = helper2(v, u)', ...                              % 32 u INUSD, r STOUT
    'disp(v)', ...                                                 % 33
    'end', ...                                                     % 34
    '', ...                                                        % 35
    'function helper3(a, b)', ...                                  % 36 DEFNU; a INUSL?
    'disp(b)', ...                                                 % 37
    'end', ...                                                     % 38
    '', ...                                                        % 39
    'function r = helper4()', ...                                  % 40
    'if rand > 0.5', ...                                           % 41
    '    m = 1;', ...                                              % 42
    'end', ...                                                     % 43
    'r = m;', ...                                                  % 44 NODEF?
    'r = r + k;', ...                                              % 45 NODEF (k defined later)
    'k = 2;', ...                                                  % 46 NASGU
    'end', ...                                                     % 47
    '', ...                                                        % 48
    'function r = helper5(v)', ...                                 % 49
    'r = v;', ...                                                  % 50
    'return', ...                                                  % 51
    'disp(v)', ...                                                 % 52 UNRCH
    'end', ...                                                     % 53
    '', ...                                                        % 54
    'function r = helper6(n)', ...                                 % 55
    'r = [];', ...                                                 % 56
    'for k = 1:n', ...                                             % 57
    '    r(end+1) = k;', ...                                       % 58 AGROW
    '    r = [r k];', ...                                          % 59 AGROW
    '    if k > 2', ...                                            % 60
    '        break', ...                                           % 61
    '        disp(k)', ...                                         % 62 UNRCH
    '    end', ...                                                 % 63
    'end', ...                                                     % 64
    'end', ...                                                     % 65
    '', ...                                                        % 66
    'function helper7(h)', ...                                     % 67
    'for k = 1:3', ...                                             % 68
    '    axes(h);', ...                                            % 69 LAXES
    '    plot(k)', ...                                             % 70
    'end', ...                                                     % 71
    'end'});                                                       % 72

writeFile(fullfile(work, 'a2_patterns.m'), { ...
    '% a2_patterns: a script of patterns', ...                     % 1
    'x = 1; a = 2; b = 3; s = ''abc''; st.f = 1; v = [1 2 3];', ...% 2
    'c1 = char(10);', ...                                          % 3  CHARTEN
    'c2 = [''a'' char(10) ''b''];', ...                            % 4  CHARTEN
    'c3 = char(13);', ...                                          % 5
    'f1 = feval(''sin'', 1);', ...                                 % 6  FVAL
    'f2 = feval(@sin, 1);', ...                                    % 7  FVAL?
    'fn = ''sin''; f3 = feval(fn, 1);', ...                        % 8
    'e1 = exist(''x'');', ...                                      % 9  EXIST
    'e2 = exist(''x'', ''var'');', ...                             % 10
    'i1 = length(v) == 1;', ...                                    % 11 ISCL
    'i2 = 1 == length(v);', ...                                    % 12 ISCL?
    'i3 = length(v) ~= 1;', ...                                    % 13 ISCL?
    'i4 = numel(v) == 1;', ...                                     % 14
    'd1 = datestr(now);', ...                                      % 15 DATST
    'd2 = datenum(2020, 1, 1);', ...                               % 16 DATNM
    'd3 = date;', ...                                              % 17 DATE
    'h1 = hist(v);', ...                                           % 18 HIST
    'hist(v)', ...                                                 % 19 HIST
    'q1 = quad(@sin, 0, 1);', ...                                  % 20 DQUAD
    'q2 = quadl(@sin, 0, 1);', ...                                 % 21 DQUADL
    'q3 = quadv(@sin, 0, 1);', ...                                 % 22 DQUADV
    'q4 = dblquad(@(x, y) x + y, 0, 1, 0, 1);', ...                % 23 DDBLQD
    'q5 = triplequad(@(x, y, z) x + y + z, 0, 1, 0, 1, 0, 1);', ...% 24 DTRIQD
    'compass(1, 2)', ...                                           % 25 COMPASS
    'in1 = inline(''x+1'');', ...                                  % 26 DINLN
    'fc = fcnchk(''x+1'');', ...                                   % 27 DFCNCHK
    'n1 = str2num(''5'');', ...                                    % 28 ST2NM
    'n2 = str2num(s);', ...                                        % 29 ST2NM?
    's1 = string(''abc'');', ...                                   % 30 STRQUOT
    's2 = string(s);', ...                                         % 31
    'g1 = getfield(st, ''f'');', ...                               % 32 GFLD
    'st = setfield(st, ''f'', 2);', ...                            % 33 SFLD
    'm1 = ndims(v) == 2;', ...                                     % 34 ISMAT?
    'm2 = length(size(v)) == 2;', ...                              % 35 ISMAT?
    'l1 = logical(1);', ...                                        % 36 LOGL
    'l2 = logical(0);', ...                                        % 37 LOGL
    'r1 = repmat(1, 2, 3);', ...                                   % 38 RPMT1
    'r2 = repmat(true, 2, 3);', ...                                % 39 RPMTT
    'r3 = repmat(0, 2, 3);', ...                                   % 40 RPMT0?
    'r4 = repmat(false, 2, 3);', ...                               % 41 RPMTF?
    't1 = strcmp(upper(s), upper(''ABC''));', ...                  % 42 STCI
    't2 = strcmp(lower(s), ''abc'');', ...                         % 43 STCI
    't3 = strcmpi(upper(s), ''ABC'');', ...                        % 44
    'k1 = [1];', ...                                               % 45 NBRAK2
    'k2 = [x];', ...                                               % 46 NBRAK2
    '[k3] = size(v);', ...                                         % 47 NBRAK2?
    'k4 = [x, a];', ...                                            % 48
    'k5 = [x,];', ...                                              % 49 NOCOMMA?
    'k6 = 1,;', ...                                                % 50 NOCOMMA?
    'k7 = 2,', ...                                                 % 51
    'for (ii = 1:3)', ...                                          % 52 NO4LP
    '    disp(ii)', ...                                            % 53
    'end', ...                                                     % 54
    'clear', ...                                                   % 55 CLEAR0ARGS
    'clear all', ...                                               % 56 CLALL?
    'clear variables', ...                                         % 57
    'clear x', ...                                                 % 58
    'load', ...                                                    % 59 LOAD?
    'load a.mat', ...                                              % 60 LOAD
    'load(''a.mat'')', ...                                         % 61 LOAD
    'dd = load(''a.mat'');', ...                                   % 62
    'eval(''x = 1;'')', ...                                        % 63 EVLCS
    'eval([''x = '' ''1;''])', ...                                 % 64 EVLCS?
    'eval([''load '' s])', ...                                     % 65 EVLDUAL
    'eval(s)', ...                                                 % 66
    'z1 = size(a + b);', ...                                       % 67 ELARLOG
    'z2 = size(a .* b);', ...                                      % 68 ELARLOG
    'z3 = numel(a + b);', ...                                      % 69 ?
    'z4 = size(a, 1) + b;', ...                                    % 70
    'ans = 1;', ...                                                % 71 NOANS
    'y1 = ans;', ...                                               % 72 NOANS?
    'global GS', ...                                               % 73 GVMIS
    'global GS', ...                                               % 74 REDEFGG
    '[1 2]', ...                                                   % 75 VUNUS
    '{1}', ...                                                     % 76 VUNUS
    'v(2);', ...                                                   % 77 NOEFF
    'x;', ...                                                      % 78 ?
    'x + 1;', ...                                                  % 79 NOEFF?
    'subsasgn(st, substruct(''.'', ''f''), 3);', ...               % 80 SUBSASGN
    'ls1 = sum(v > 1);', ...                                       % 81 LOGSUM?
    'disp(sprintf(''%d'', x))', ...                                % 82 DSPS?
    'nw = now;', ...                                               % 83 NOW?
    'if x == 1, disp(1), end', ...                                 % 84
    'if (x == 1) disp(2), end', ...                                % 85 ?
    'while (x < 0) x = x + 1; end', ...                            % 86 ?
    'fprintf(''%s\n'', num2str(x));', ...                          % 87 ?
    'cc = cellfun(@(c) c, {1}, ''UniformOutput'', 0);', ...        % 88 ?
    'xx = x'';', ...                                               % 89
    'yy = isempty(v) == 0;', ...                                   % 90 ?
    'ss = strrep(s, ''a'', ''b'');', ...                           % 91
    'switch x', ...                                                % 92
    '    case 1', ...                                              % 93
    '        disp(1)', ...                                         % 94
    '    case 1', ...                                              % 95 dup?
    '        disp(2)', ...                                         % 96
    'end'});                                                       % 97

writeFile(fullfile(work, 'A3Class.m'), { ...
    'classdef A3Class < handle', ...                               % 1
    '    properties', ...                                          % 2
    '        P = 1', ...                                           % 3
    '    end', ...                                                 % 4
    '    methods', ...                                             % 5
    '        function r = m1(obj, a)', ...                         % 6  a MANU
    '            r = obj.P;', ...                                  % 7
    '        end', ...                                             % 8
    '        function r = m2(obj)', ...                            % 9  obj MANU
    '            r = 2;', ...                                      % 10
    '        end', ...                                             % 11
    '        function r = m3(obj, a)', ...                         % 12 r STOUT
    '            disp(obj.P + a)', ...                             % 13
    '        end', ...                                             % 14
    '        function set.P(obj, v)', ...                          % 15
    '            obj.P = v;', ...                                  % 16
    '        end', ...                                             % 17
    '    end', ...                                                 % 18
    '    methods (Static)', ...                                    % 19
    '        function r = s1(a, b)', ...                           % 20 b INUSD
    '            r = a;', ...                                      % 21
    '        end', ...                                             % 22
    '    end', ...                                                 % 23
    'end'});                                                       % 24

writeFile(fullfile(work, 'a4_error.m'), { ...
    'function a4_error()', ...
    'x = (1 + ;', ...
    'disp(x)', ...
    'end'});

writeFile(fullfile(work, 'a5_suppress.m'), { ...
    'function a5_suppress(a, b) %#ok<INUSD>', ...                 % 1  both suppressed?
    '%#ok<*NASGU>', ...                                            % 2  file-wide
    'x = 1;', ...                                                  % 3  suppressed by *NASGU
    'y = char(10); %#ok<CHARTEN,NASGU>', ...                       % 4  CHARTEN suppressed, NASGU note?
    'z = char(10); %#ok<INUSD>', ...                               % 5  CHARTEN stays, MSNU
    'w = 1; %#ok<NASGU> a comment after', ...                      % 6
    'q = 1;  %#ok  <NASGU>', ...                                   % 7  the space form?
    'end'});

writeFile(fullfile(work, 'a6_scopes.m'), { ...
    'function r = a6_scopes(a, b, varargin)', ...                  % 1
    'if nargin < 2', ...                                           % 2
    '    b = 1;', ...                                              % 3
    'end', ...                                                     % 4
    'f = @(t) t + a;', ...                                         % 5  a used in anonymous fn
    'r = f(b) + nested1();', ...                                   % 6
    'for k = 1:3', ...                                             % 7  k unused?
    '    disp(1)', ...                                             % 8
    'end', ...                                                     % 9
    'try', ...                                                     % 10
    '    c = 1;', ...                                              % 11
    'catch err', ...                                               % 12 err unused?
    '    c = 2;', ...                                              % 13
    'end', ...                                                     % 14
    'disp(c)', ...                                                 % 15
    'ev = 1;', ...                                                 % 16 NASGU with eval below?
    'eval(''disp(ev)'')', ...                                      % 17 EVLCS
    'hold on', ...                                                 % 18 command syntax
    'shared = 5;', ...                                             % 19
    '    function n = nested1()', ...                              % 20
    '        n = shared;', ...                                     % 21
    '    end', ...                                                 % 22
    'end', ...                                                     % 23
    '', ...                                                        % 24
    'function varargout = a6_local(varargin)', ...                 % 25 DEFNU
    'varargout{1} = numel(varargin);', ...                         % 26
    'end'});

writeFile(fullfile(work, 'a7_cyc.m'), { ...
    'function r = a7_cyc(x)', ...
    'if x > 1', ...
    '    r = 1;', ...
    'elseif x > 0', ...
    '    r = 2;', ...
    'else', ...
    '    r = 3;', ...
    'end', ...
    'for k = 1:x', ...
    '    r = r + k;', ...
    'end', ...
    'end', ...
    '', ...
    'function helper(a)', ...
    'disp(a)', ...
    'end'});

writeFile(fullfile(work, 'a8_clean.m'), { ...
    'function r = a8_clean(x)', ...
    'r = x + 1;', ...
    'end'});

addpath(work);
cd(work);

% --- the messages, file by file -------------------------------------------------------------------
files = {'a1_unused', 'a2_patterns', 'A3Class', 'a4_error', 'a5_suppress', 'a6_scopes', 'a7_cyc', 'a8_clean'};
if exist(fullfile(work, 'AcceptFormPlain.m'), 'file')
    files = [files {'AcceptFormPlain', 'AcceptForm.mlapp'}];
end
for k = 1:numel(files)
    fprintf('== messages: %s\n', files{k});
    try
        m = checkcode(files{k}, '-id', '-struct');
        printMessages(files{k}, m);
    catch e
        fprintf('ERR|%s|%s\n', e.identifier, short(e.message));
    end
end

fprintf('== -severity: a1_unused\n');
try
    m = checkcode('a1_unused', '-id', '-struct', '-severity');
    fprintf('fields: %s\n', strjoin(fieldnames(m)', ','));
    for i = 1:numel(m)
        fprintf('SEV|%s|%d|%d\n', m(i).id, m(i).line(1), m(i).severity);
    end
catch e
    fprintf('ERR|%s|%s\n', e.identifier, short(e.message));
end

% --- the display forms ----------------------------------------------------------------------------
show('display: checkcode(''a8_clean'')', @() evalc('checkcode(''a8_clean'')'), short);
show('display: checkcode(''a7_cyc'')', @() evalc('checkcode(''a7_cyc'')'), short);
show('display: checkcode a7_cyc -id', @() evalc('checkcode a7_cyc -id'), short);
show('display: checkcode(''a7_cyc'', ''-fullpath'')', @() evalc('checkcode(''a7_cyc'', ''-fullpath'')'), short);
show('display: checkcode(''a7_cyc.m'')', @() evalc('checkcode(''a7_cyc.m'')'), short);
show('display: checkcode(fullfile(work, ''a7_cyc.m''))', @() evalc('checkcode(fullfile(work, ''a7_cyc.m''))'), short);
show('display: checkcode(''a7_cyc'', ''-cyc'')', @() evalc('checkcode(''a7_cyc'', ''-cyc'')'), short);
show('display: checkcode(''a7_cyc'', ''-modcyc'')', @() evalc('checkcode(''a7_cyc'', ''-modcyc'')'), short);
show('display: checkcode(''a7_cyc'', ''-cyc'', ''-id'')', @() evalc('checkcode(''a7_cyc'', ''-cyc'', ''-id'')'), short);
show('display: checkcode(''a5_suppress'', ''-notok'')', @() evalc('checkcode(''a5_suppress'', ''-notok'')'), short);
show('display: checkcode(''a5_suppress'', ''-notok'', ''-id'')', @() evalc('checkcode(''a5_suppress'', ''-notok'', ''-id'')'), short);
show('display: checkcode({''a7_cyc'', ''a8_clean''})', @() evalc('checkcode({''a7_cyc'', ''a8_clean''})'), short);
show('display: checkcode({''a7_cyc'', ''a8_clean''}, ''-id'')', @() evalc('checkcode({''a7_cyc'', ''a8_clean''}, ''-id'')'), short);
show('display: checkcode(''a7_cyc'', ''a8_clean'')', @() evalc('checkcode(''a7_cyc'', ''a8_clean'')'), short);
show('display: checkcode(''a4_error'')', @() evalc('checkcode(''a4_error'')'), short);
show('display: checkcode(''A3Class'')', @() evalc('checkcode(''A3Class'')'), short);
show('display: mlint(''a7_cyc'')', @() evalc('mlint(''a7_cyc'')'), short);
show('display: checkcode(''a7_cyc'', ''-disp'')', @() evalc('checkcode(''a7_cyc'', ''-disp'')'), short);
show('display: checkcode(''a7_cyc'', ''-config=factory'')', @() evalc('checkcode(''a7_cyc'', ''-config=factory'')'), short);
show('display: checkcode(''a7_cyc'', ''-eml'')', @() evalc('checkcode(''a7_cyc'', ''-eml'')'), short);
show('display: checkcode(''a7_cyc'', ''-codegen'')', @() evalc('checkcode(''a7_cyc'', ''-codegen'')'), short);
show('display: -text form A', @() evalc('checkcode(''-text'', sprintf(''function t()\nx = 1;\nend\n''), ''t.m'')'), short);
show('display: -text form B', @() evalc('checkcode(sprintf(''function t()\nx = 1;\nend\n''), ''-text'', ''t.m'')'), short);
show('display: -text form C', @() evalc('checkcode(''-text'', sprintf(''function t()\nx = 1;\nend\n''))'), short);

fprintf('== the outputs\n');
try
    s = checkcode('a7_cyc', '-string');
    fprintf('string class %s size %s\n', class(s), mat2str(size(s)));
    fprintf('%s', short(s)); fprintf('<EOS>\n');
    s = checkcode('a7_cyc', '-string', '-id');
    fprintf('%s', short(s)); fprintf('<EOS>\n');
    s = checkcode('a8_clean', '-string');
    fprintf('empty string class %s size %s [%s]\n', class(s), mat2str(size(s)), s);
    s = checkcode({'a7_cyc', 'a8_clean'}, '-string');
    fprintf('cell string class %s size %s\n', class(s), mat2str(size(s)));
    for i = 1:numel(s), fprintf('{%d}: %s<EOS>\n', i, short(s{i})); end
    [m, f] = checkcode('a7_cyc');
    fprintf('two outputs: m %s %s, f %s %s = %s\n', class(m), mat2str(size(m)), class(f), mat2str(size(f)), short(strjoin(f, ';')));
    fprintf('fields: %s\n', strjoin(fieldnames(m)', ','));
    fprintf('message %s, line %s %s, column %s %s, fix %s %s\n', class(m(1).message), class(m(1).line), mat2str(m(1).line), class(m(1).column), mat2str(m(1).column), class(m(1).fix), mat2str(m(1).fix));
    m = checkcode('a7_cyc', '-id');
    fprintf('fields -id: %s\n', strjoin(fieldnames(m)', ','));
    m = checkcode('a8_clean');
    fprintf('clean struct: %s size %s fields %s\n', class(m), mat2str(size(m)), strjoin(fieldnames(m)', ','));
    m = checkcode('a8_clean', '-id');
    fprintf('clean struct -id: %s size %s fields %s\n', class(m), mat2str(size(m)), strjoin(fieldnames(m)', ','));
    m = checkcode({'a7_cyc', 'a8_clean'});
    fprintf('cell struct: %s size %s; {1} %s %s; {2} %s %s\n', class(m), mat2str(size(m)), class(m{1}), mat2str(size(m{1})), class(m{2}), mat2str(size(m{2})));
    [m, f] = checkcode({'a7_cyc', 'a8_clean'});
    fprintf('cell two outputs: f %s %s = %s\n', class(f), mat2str(size(f)), short(strjoin(f, ';')));
    m = checkcode({'a7_cyc'});
    fprintf('one-cell struct: %s size %s\n', class(m), mat2str(size(m)));
    m = checkcode('a7_cyc', '-cyc');
    printMessages('a7_cyc -cyc', m);
    m = checkcode('a7_cyc', '-cyc', '-id');
    printMessages('a7_cyc -cyc -id', m);
    m = checkcode('a5_suppress', '-notok', '-id');
    printMessages('a5_suppress -notok -id', m);
    m = checkcode('a1_unused', '-id', '-fullpath');
    fprintf('fullpath form: %d messages\n', numel(m));
    m = checkcode("a7_cyc", "-id");
    fprintf('string args: %d messages, class %s\n', numel(m), class(m));
    m = mlint('a7_cyc', '-id');
    fprintf('mlint struct: %d messages; lastwarn [%s]\n', numel(m), lastwarn);
    m = checkcode('a7_cyc', '-struct');
    fprintf('explicit -struct: %s %d\n', class(m), numel(m));
    m = checkcode('a4_error', '-id');
    printMessages('a4_error struct', m);
catch e
    fprintf('ERR|%s|%s\n', e.identifier, short(e.message));
end

fprintf('== refusals\n');
refuse('checkcode()', @() checkcode(), short);
refuse('checkcode(''nosuch_w10'')', @() checkcode('nosuch_w10'), short);
refuse('checkcode(''nosuch_w10.m'')', @() checkcode('nosuch_w10.m'), short);
refuse('checkcode(5)', @() checkcode(5), short);
refuse('checkcode('''')', @() checkcode(''), short);
refuse('checkcode({})', @() checkcode({}), short);
refuse('x = checkcode({})', @() evalc('x = checkcode({}); disp(class(x)); disp(size(x))'), short);
refuse('checkcode(''-id'')', @() checkcode('-id'), short);
refuse('checkcode({''a7_cyc''}, ''a8_clean'')', @() evalc('checkcode({''a7_cyc''}, ''a8_clean'')'), short);
refuse('checkcode({''a7_cyc''}, {''a8_clean''})', @() checkcode({'a7_cyc'}, {'a8_clean'}), short);
refuse('checkcode({{''a7_cyc''}})', @() checkcode({{'a7_cyc'}}), short);
refuse('checkcode({''a7_cyc'', 5})', @() checkcode({'a7_cyc', 5}), short);
refuse('checkcode(''a7_cyc'', ''-bogus'')', @() evalc('checkcode(''a7_cyc'', ''-bogus'')'), short);
refuse('[a,b,c] = checkcode(''a7_cyc'')', @() evalc('[a,b,c] = checkcode(''a7_cyc'')'), short);
refuse('x = checkcode(''a7_cyc'', ''-disp'')', @() evalc('x = checkcode(''a7_cyc'', ''-disp'')'), short);
refuse('checkcode(string(missing))', @() checkcode(string(missing)), short);
refuse('checkcode([''a''; ''b''])', @() checkcode(['ab'; 'cd']), short);
refuse('checkcode(''a7_cyc'', ''a8_clean'', ''-string'') outputs', @() evalc('x = checkcode(''a7_cyc'', ''a8_clean'', ''-string''); disp(class(x)); disp(size(x))'), short);
refuse('checkcode(work) folder', @() checkcode(work), short);
refuse('checkcode(''a2_patterns.txt'')', @() checkcode('a2_patterns.txt'), short);
refuse('mlint()', @() mlint(), short);

% --- codeIssues ----------------------------------------------------------------------------------
fprintf('== codeIssues\n');
try
    c = codeIssues(fullfile(work, 'a7_cyc.m'));
    fprintf('class %s\n', class(c));
    fprintf('--- display c\n%s<EOS>\n', short(evalc('c')));
    fprintf('--- disp(c)\n%s<EOS>\n', short(evalc('disp(c)')));
    fprintf('Date class %s; Release %s %s; Files %s %s %s; Configuration %s %s\n', class(c.Date), class(c.Release), c.Release, class(c.Files), mat2str(size(c.Files)), short(strjoin(c.Files, ';')), class(c.CodeAnalyzerConfiguration), c.CodeAnalyzerConfiguration);
    t = c.Issues;
    fprintf('Issues %s %s vars %s\n', class(t), mat2str(size(t)), strjoin(t.Properties.VariableNames, ','));
    for j = 1:width(t), fprintf('  %s: %s %s\n', t.Properties.VariableNames{j}, class(t.(j)), mat2str(size(t.(j)))); end
    fprintf('--- Issues display\n%s<EOS>\n', short(evalc('disp(t)')));
    for i = 1:height(t)
        fprintf('ISSUE|%s|%s|%s|%s|%s|%d|%d|%d|%d|%s\n', t.Location(i), string(t.Severity(i)), string(t.Fixability(i)), t.Description(i), string(t.CheckID(i)), t.LineStart(i), t.LineEnd(i), t.ColumnStart(i), t.ColumnEnd(i), short(t.FullFilename(i)));
    end
    s = c.SuppressedIssues;
    fprintf('SuppressedIssues %s %s vars %s\n', class(s), mat2str(size(s)), strjoin(s.Properties.VariableNames, ','));
    c5 = codeIssues(fullfile(work, 'a5_suppress.m'));
    s = c5.SuppressedIssues;
    fprintf('--- a5 SuppressedIssues display\n%s<EOS>\n', short(evalc('disp(s)')));
    for i = 1:height(s)
        fprintf('SUPP|%s|%s|%s|%s|%s|%d|%d|%d|%d\n', string(s.Suppression(i)), string(s.Severity(i)), string(s.Fixability(i)), s.Description(i), string(s.CheckID(i)), s.LineStart(i), s.LineEnd(i), s.ColumnStart(i), s.ColumnEnd(i));
    end
    fprintf('--- a5 display\n%s<EOS>\n', short(evalc('c5')));
    c8 = codeIssues(fullfile(work, 'a8_clean.m'));
    fprintf('--- a8 (clean) display\n%s<EOS>\n', short(evalc('c8')));
    fprintf('clean Issues %s; Suppressed %s\n', mat2str(size(c8.Issues)), mat2str(size(c8.SuppressedIssues)));
    cf = codeIssues(work);
    fprintf('folder: Files %s = %s\n', mat2str(size(cf.Files)), short(strjoin(cf.Files, ';')));
    fprintf('folder issues %d; by file: %s\n', height(cf.Issues), strjoin(cellstr(string(unique(cf.Issues.Location, 'stable'))'), ','));
    fprintf('--- folder display (head)\n%s<EOS>\n', short(extractBefore(string(evalc('cf')) + blanks(2500), 2500)));
    cm = codeIssues({fullfile(work, 'a7_cyc.m'), fullfile(work, 'a8_clean.m')});
    fprintf('cell: Files %s = %s\n', mat2str(size(cm.Files)), short(strjoin(cm.Files, ';')));
    cwd = codeIssues();
    fprintf('no args: Files %d\n', numel(cwd.Files));
    c4 = codeIssues(fullfile(work, 'a4_error.m'));
    fprintf('--- a4 (error) issues\n');
    t = c4.Issues;
    for i = 1:height(t)
        fprintf('ISSUE|%s|%s|%s|%s|%s|%d|%d|%d|%d\n', t.Location(i), string(t.Severity(i)), string(t.Fixability(i)), t.Description(i), string(t.CheckID(i)), t.LineStart(i), t.LineEnd(i), t.ColumnStart(i), t.ColumnEnd(i));
    end
    c1 = codeIssues(fullfile(work, 'a1_unused.m'));
    t = c1.Issues;
    fprintf('--- a1 issues (codeIssues descriptions)\n');
    for i = 1:height(t)
        fprintf('ISSUE|%s|%s|%s|%s|%s|%d|%d|%d|%d\n', t.Location(i), string(t.Severity(i)), string(t.Fixability(i)), t.Description(i), string(t.CheckID(i)), t.LineStart(i), t.LineEnd(i), t.ColumnStart(i), t.ColumnEnd(i));
    end
    fprintf('--- a1 display (many rows)\n%s<EOS>\n', short(evalc('c1')));
    c2 = codeIssues(fullfile(work, 'a2_patterns.m'));
    t = c2.Issues;
    fprintf('--- a2 issues (codeIssues descriptions)\n');
    for i = 1:height(t)
        fprintf('ISSUE|%s|%s|%s|%s|%s|%d|%d|%d|%d\n', t.Location(i), string(t.Severity(i)), string(t.Fixability(i)), t.Description(i), string(t.CheckID(i)), t.LineStart(i), t.LineEnd(i), t.ColumnStart(i), t.ColumnEnd(i));
    end
    fprintf('--- a2 display (many rows)\n%s<EOS>\n', short(evalc('c2')));
    fprintf('methods: %s\n', strjoin(methods(c)', ','));
    fprintf('properties: %s\n', strjoin(properties(c)', ','));
    % the fix method, on a copy
    fixDir = fullfile(work, 'fix');
    mkdir(fixDir);
    copyfile(fullfile(work, 'a2_patterns.m'), fixDir);
    cfx = codeIssues(fullfile(fixDir, 'a2_patterns.m'));
    before = height(cfx.Issues);
    [cfx, status] = fix(cfx, 'CHARTEN');
    fprintf('fix CHARTEN: %d -> %d issues; status %s %s vars %s\n', before, height(cfx.Issues), class(status), mat2str(size(status)), strjoin(status.Properties.VariableNames, ','));
    fprintf('--- fixed lines 3-4\n');
    L = readlines(fullfile(fixDir, 'a2_patterns.m'));
    fprintf('%s\n', L(3:4));
    cfx = fix(cfx, ["NBRAK2", "LOGL", "STRQUOT", "RPMT1", "RPMTT", "STCI", "FVAL", "NOCOMMA"]);
    L = readlines(fullfile(fixDir, 'a2_patterns.m'));
    fprintf('--- after more fixes: lines 6-7, 30, 36-39, 42-43, 45-47, 49-51\n');
    fprintf('%s\n', L([6 7 30 36 37 38 39 42 43 45 46 47 49 50 51]));
    copyfile(fullfile(work, 'a1_unused.m'), fixDir);
    cfx = codeIssues(fullfile(fixDir, 'a1_unused.m'));
    cfx = fix(cfx, ["INUSD", "ASGLU", "NUSED", "MSNU"]);
    L = readlines(fullfile(fixDir, 'a1_unused.m'));
    fprintf('--- a1 after fixes: lines 1, 4, 13, 26, 32\n');
    fprintf('%s\n', L([1 4 13 26 32]));
    fprintf('remaining: %s\n', strjoin(cellstr(string(cfx.Issues.CheckID))', ','));
catch e
    fprintf('ERR|%s|%s\n', e.identifier, short(getReport(e, 'basic')));
end
fprintf('== codeIssues refusals\n');
refuse('codeIssues(''nosuch_w10.m'')', @() codeIssues('nosuch_w10.m'), short);
refuse('codeIssues(''a7_cyc'')', @() evalc('q = codeIssues(''a7_cyc''); disp(q.Files)'), short);
refuse('codeIssues(5)', @() codeIssues(5), short);
refuse('codeIssues('''')', @() codeIssues(''), short);
refuse('codeIssues(''a2_patterns.txt'')', @() codeIssues('a2_patterns.txt'), short);

fprintf('== nargout and exist\n');
for n = {'checkcode', 'mlint', 'codeIssues'}
    try
        fprintf('%s: nargout %d exist %d which %s\n', n{1}, nargout(n{1}), exist(n{1}), short(which(n{1})));
    catch e
        fprintf('%s: ERR %s\n', n{1}, e.message);
    end
end
fprintf('done\n');

% --- helpers ------------------------------------------------------------------------------------------
function writeFile(path, lines)
f = fopen(path, 'w');
fprintf(f, '%s\n', lines{:});
fclose(f);
end

function printMessages(label, m)
if iscell(m)
    for i = 1:numel(m), printMessages(sprintf('%s{%d}', label, i), m{i}); end
    return
end
for i = 1:numel(m)
    id = '';
    if isfield(m, 'id'), id = m(i).id; end
    fixv = '';
    if isfield(m, 'fix'), fixv = mat2str(m(i).fix); end
    fprintf('MSG|%s|%s|%s|%s|%s|%s\n', label, id, mat2str(m(i).line), mat2str(m(i).column), fixv, m(i).message);
end
if isempty(m), fprintf('MSG|%s|none\n', label); end
end

function show(label, fn, short)
fprintf('== %s\n', label);
try
    t = fn();
    fprintf('%s<EOS>\n', short(t));
catch e
    fprintf('ERR|%s|%s\n', e.identifier, short(e.message));
end
end

function refuse(label, fn, short)
try
    r = fn();
    if ischar(r), fprintf('ACCEPTED: %s -> %s<EOS>\n', label, short(r)); else, fprintf('ACCEPTED: %s\n', label); end
catch e
    fprintf('REFUSED: %s -> %s: %s\n', label, e.identifier, short(e.message));
end
end
