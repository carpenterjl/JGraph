% w10_rules - the second W10 probe: what w10_analyzer left open. The struct's fields and classes, two
% outputs, the cell and one-cell forms, -cyc and -notok as structs, the multi-file -string text byte for
% byte, the TooManyOutputArguments refusal, an absolute path's display; and the rules behind the checks
% (DEFNU beside eval, clear and load in a function, the comma checks, STCI's two sides, ISCL's forms,
% ELARLOG's functions and operators, eval's three ids, NASGU's edges, NODEF across a loop, UNRCH after
% error and continue, AGROW's forms, STOUT in one branch, NOEFF and VUNUS in a function, the global and
% persistent edges, DEFNU for nested functions and handles, %#ok with an unknown id). Headless.
%   powershell -ExecutionPolicy Bypass -File .\run-probe.ps1 -Name w10_rules
here = fileparts(mfilename('fullpath'));
work = fullfile(here, 'rt', 'rules');
if exist(work, 'dir'), rmdir(work, 's'); end
mkdir(work);
short = @(s) strrep(strrep(char(s), work, '<work>'), strrep(work, '\', '/'), '<work>');

writeFile(fullfile(work, 'b1_defnu.m'), { ...
    'function b1_defnu()', ...                          % 1
    'eval(''disp(1)'')', ...                             % 2
    'end', ...                                           % 3
    'function unused1()', ...                            % 4  DEFNU with eval in the file?
    'end'});
writeFile(fullfile(work, 'b2_defnu.m'), { ...
    'function b2_defnu()', ...                           % 1
    'f = @unused2;', ...                                 % 2  handle use
    'cellfun(@unused3, {1});', ...                       % 3
    'feval(''unused4'');', ...                           % 4  by name in a string
    'disp(f)', ...                                       % 5
    '    function nestedUnused()', ...                   % 6  nested, never called
    '    end', ...                                       % 7
    'end', ...                                           % 8
    'function unused2()', ...                            % 9
    'end', ...                                           % 10
    'function unused3(a)', ...                           % 11
    'disp(a)', ...                                       % 12
    'end', ...                                           % 13
    'function unused4()', ...                            % 14
    'end', ...                                           % 15
    'function unused5(varargin)', ...                    % 16 DEFNU; varargin unused?
    'end'});
writeFile(fullfile(work, 'b3_clearload.m'), { ...
    'function b3_clearload()', ...                       % 1
    'clear', ...                                         % 2  CLEAR0ARGS?
    'clear all', ...                                     % 3
    'clear variables', ...                               % 4
    'clear x', ...                                       % 5
    'clear global', ...                                  % 6
    'clear functions', ...                               % 7
    'clear(''y'')', ...                                  % 8
    'load a.mat', ...                                    % 9  LOAD
    'load(''a.mat'')', ...                               % 10 LOAD
    'load a.mat x', ...                                  % 11
    'load(''a.mat'', ''x'')', ...                        % 12
    'd = load(''a.mat'');', ...                          % 13
    'disp(d)', ...                                       % 14
    'end'});
writeFile(fullfile(work, 'b4_comma.m'), { ...
    'x = [1, 2,];', ...                                  % 1
    'y = {1, 2,};', ...                                  % 2
    'z = [1, 2; 3, 4,];', ...                            % 3
    'w = [1, 2,; 3, 4];', ...                            % 4
    'disp(x), disp(y),', ...                             % 5
    'disp(z);,', ...                                     % 6
    'for k = 1:2, disp(k), end,', ...                    % 7
    'if w, disp(1), end;', ...                           % 8
    'q = 1;;', ...                                       % 9
    'disp(q);'});
writeFile(fullfile(work, 'b5_stci.m'), { ...
    'function b5_stci(a, b)', ...                        % 1
    't1 = strcmp(upper(a), upper(b));', ...              % 2  STCI
    't2 = strcmp(lower(a), b);', ...                     % 3  ?
    't3 = strcmp(upper(a), ''ABC'');', ...               % 4  ?
    't4 = strcmp(upper(a), ''abc'');', ...               % 5  ?
    't5 = strcmpi(upper(a), b);', ...                    % 6  STLOW
    't6 = strcmpi(a, lower(b));', ...                    % 7  STLOW
    't7 = strcmp(lower(a), lower(b));', ...              % 8  STCI
    't8 = strncmp(upper(a), upper(b), 2);', ...          % 9  ?
    't9 = isequal(upper(a), upper(b));', ...             % 10 ?
    'disp([t1 t2 t3 t4 t5 t6 t7 t8 t9])', ...            % 11
    'end'});
writeFile(fullfile(work, 'b6_iscl.m'), { ...
    'function b6_iscl(v)', ...                           % 1
    'i1 = numel(v) ~= 1;', ...                           % 2
    'i2 = length(v) > 1;', ...                           % 3
    'i3 = 1 ~= numel(v);', ...                           % 4
    'i4 = isequal(length(v), 1);', ...                   % 5
    'i5 = length(v) == 0;', ...                          % 6
    'i6 = numel(v) == 1 && v > 0;', ...                  % 7
    'i7 = size(v, 1) == 1;', ...                         % 8
    'i8 = ndims(v) ~= 2;', ...                           % 9
    'i9 = ndims(v) > 2;', ...                            % 10
    'disp([i1 i2 i3 i4 i5 i6 i7 i8 i9])', ...            % 11
    'end'});
writeFile(fullfile(work, 'b7_elarlog.m'), { ...
    'function b7_elarlog(a, b)', ...                     % 1
    'z1 = length(a + b);', ...                           % 2
    'z2 = ndims(a - b);', ...                            % 3
    'z3 = size(a * b);', ...                             % 4
    'z4 = size(a / b);', ...                             % 5
    'z5 = numel(a .^ b);', ...                           % 6
    'z6 = size(a == b);', ...                            % 7
    'z7 = size(a & b);', ...                             % 8
    'z8 = size(-a);', ...                                % 9
    'z9 = size(a'');', ...                               % 10
    'z10 = size(a + b, 1);', ...                         % 11
    'z11 = isempty(a + b);', ...                         % 12
    'z12 = size([a b]);', ...                            % 13
    'disp({z1 z2 z3 z4 z5 z6 z7 z8 z9 z10 z11 z12})', ...% 14
    'end'});
writeFile(fullfile(work, 'b8_eval.m'), { ...
    'function b8_eval(s)', ...                           % 1
    'eval(''a'')', ...                                   % 2
    'eval([''x='' num2str(1)])', ...                     % 3
    'eval([''disp('' s '')''])', ...                     % 4
    'eval(''y = 2'')', ...                               % 5
    'eval(s, ''disp(0)'')', ...                          % 6
    'evalin(''base'', ''x'')', ...                       % 7
    'evalc(''disp(1)'');', ...                           % 8
    'r = eval(''1+1'');', ...                            % 9
    'disp(r)', ...                                       % 10
    'feval(@(x) x, 1);', ...                             % 11
    'feval(s, 1);', ...                                  % 12
    'end'});
writeFile(fullfile(work, 'b9_nasgu.m'), { ...
    'function out = b9_nasgu(c, f)', ...                 % 1
    'x = 1;', ...                                        % 2
    'if c', ...                                          % 3
    '    x = 2;', ...                                    % 4
    'end', ...                                           % 5
    'disp(x)', ...                                       % 6
    'for k = 1:3', ...                                   % 7
    'end', ...                                           % 8
    'disp(k)', ...                                       % 9
    '[a, b] = f();', ...                                 % 10 ASGLU a
    'disp(b)', ...                                       % 11
    '[~, b2] = f();', ...                                % 12
    'disp(b2)', ...                                      % 13
    'p(1).f = 2;', ...                                   % 14 STRNU?
    'q.f(2) = 3;', ...                                   % 15 STRNU?
    'cl{1} = 2;', ...                                    % 16 NASGU? cell
    'n = 0;', ...                                        % 17
    'for k2 = 1:3', ...                                  % 18
    '    n = n + 1;', ...                                % 19 NASGU?
    'end', ...                                           % 20
    'out = 1;', ...                                      % 21
    'out = 2;', ...                                      % 22 NASGU on an output?
    'm1 = 1; m1 = m1 + 1;', ...                          % 23
    'persistent pp', ...                                 % 24
    'if isempty(pp), pp = 0; end', ...                   % 25
    'pp = pp + 1;', ...                                  % 26
    'global gg', ...                                     % 27 NUSED? set never read
    'gg = 1;', ...                                       % 28
    'persistent p2', ...                                 % 29 NUSED (never used)
    'persistent p3', ...                                 % 30 NUSED? (set only)
    'p3 = 1;', ...                                       % 31
    'w = 5;', ...                                        % 32 used by the nested function
    'nested();', ...                                     % 33
    '    function nested()', ...                         % 34
    '        disp(w)', ...                               % 35
    '    end', ...                                       % 36
    'end', ...                                           % 37
    '', ...                                              % 38
    'function b9_varg(varargin)', ...                    % 39 varargin unused?
    'end', ...                                           % 40
    '', ...                                              % 41
    'function b9_nargin(a, b)', ...                      % 42
    'if nargin > 1, disp(1), end', ...                   % 43 a, b unused with nargin?
    'end', ...                                           % 44
    '', ...                                              % 45
    'function b9_tilde(~, b)', ...                       % 46
    'disp(b)', ...                                       % 47
    'end', ...                                           % 48
    '', ...                                              % 49
    'function r = b9_nodef()', ...                       % 50
    'for i = 1:3', ...                                   % 51
    '    if i > 1, disp(prev), end', ...                 % 52 NODEF? loop-carried
    '    prev = i;', ...                                 % 53
    'end', ...                                           % 54
    'r = i;', ...                                        % 55
    'end', ...                                           % 56
    '', ...                                              % 57
    'function b9_unrch(c)', ...                          % 58
    'if c', ...                                          % 59
    '    error(''x:y'', ''boom'');', ...                 % 60
    '    disp(1)', ...                                   % 61 UNRCH?
    'end', ...                                           % 62
    'for k = 1:3', ...                                   % 63
    '    continue', ...                                  % 64
    '    disp(k)', ...                                   % 65 UNRCH
    'end', ...                                           % 66
    'while true', ...                                    % 67
    '    disp(1)', ...                                   % 68
    'end', ...                                           % 69
    'disp(2)', ...                                       % 70 UNRCH?
    'end', ...                                           % 71
    '', ...                                              % 72
    'function x = b9_agrow(n)', ...                      % 73
    'x = [];', ...                                       % 74
    'for k = 1:n', ...                                   % 75
    '    x(k) = k;', ...                                 % 76 AGROW?
    '    x = [x; k];', ...                               % 77 AGROW
    '    x{end+1} = k;', ...                             % 78 AGROW
    '    x(end+1, :) = [1 2];', ...                      % 79 AGROW
    '    x(end+1).f = 1;', ...                           % 80 AGROW?
    '    y(k) = k;', ...                                 % 81 AGROW? y undeclared
    'end', ...                                           % 82
    'z = zeros(1, n);', ...                              % 83
    'for k = 1:n', ...                                   % 84
    '    z(k) = k;', ...                                 % 85 no
    '    z(end+1) = k;', ...                             % 86 AGROW
    'end', ...                                           % 87
    'disp(z); disp(y)', ...                              % 88
    'end', ...                                           % 89
    '', ...                                              % 90
    'function [r, s] = b9_stout(c)', ...                 % 91 s STOUT?
    'if c', ...                                          % 92
    '    r = 1;', ...                                    % 93 r set in one branch
    'end', ...                                           % 94
    'end', ...                                           % 95
    '', ...                                              % 96
    'function varargout = b9_varout()', ...              % 97 STOUT?
    'end', ...                                           % 98
    '', ...                                              % 99
    'function b9_effects(x, s, c)', ...                  % 100
    'x.f;', ...                                          % 101 NOEFF?
    'c{1};', ...                                         % 102
    's(1).a;', ...                                       % 103
    'x(1);', ...                                         % 104 NOEFF
    'sin(x);', ...                                       % 105
    'x'';', ...                                          % 106 VUNUS?
    '-x;', ...                                           % 107
    'x == 1;', ...                                       % 108
    'x(1) + 1;', ...                                     % 109
    '1:3;', ...                                          % 110
    '[1 2];', ...                                        % 111 VUNUS
    '{1};', ...                                          % 112 VUNUS
    '@sin;', ...                                         % 113
    'x', ...                                             % 114
    '[1 2]', ...                                         % 115
    'x;', ...                                            % 116
    'end', ...                                           % 117
    '', ...                                              % 118
    'function b9_ok(a) %#ok<FOO>', ...                   % 119 unknown id
    'x = 1; %#ok<*FOO>', ...                             % 120
    'y = 2; %#ok<NASGU,FOO>', ...                        % 121
    'z = 3; %#ok<NASGU> %#ok<INUSD>', ...                % 122 two pragmas?
    'disp(a)', ...                                       % 123
    'end', ...                                           % 124
    '', ...                                              % 125
    'function b9_loops(h)', ...                          % 126
    'for k = 1:3', ...                                   % 127
    '    figure(h);', ...                                % 128 LFIG?
    '    hold on', ...                                   % 129
    '    plot(k)', ...                                   % 130
    '    axes(h)', ...                                   % 131 LAXES command form
    '    while k > 5', ...                               % 132
    '        axes(h);', ...                              % 133 LAXES nested loop
    '    end', ...                                       % 134
    'end', ...                                           % 135
    'axes(h);', ...                                      % 136 no
    'end', ...                                           % 137
    '', ...                                              % 138
    'function b9_misc(v, s, x)', ...                     % 139
    'disp(sprintf(''%d'', x));', ...                     % 140 DSPSP
    'disp(sprintf(''%d\n'', x))', ...                    % 141 DSPSP?
    'c = char(10);', ...                                 % 142 CHARTEN, NASGU
    'd = [char(10)];', ...                               % 143 NBRAK2 + CHARTEN
    'e = exist(v);', ...                                 % 144 EXIST
    'g = exist(''v'', ''var'', 3);', ...                 % 145 ?
    'f = isempty(v) == 0;', ...                          % 146 ?
    'n = str2num(num2str(1));', ...                      % 147 ST2NM
    'l = length(s) == 1;', ...                           % 148 ISCL
    'r = repmat(1, 3);', ...                             % 149 RPMT1?
    'r2 = repmat(1, [2 3]);', ...                        % 150 RPMT1?
    'r3 = repmat(2, 2, 3);', ...                         % 151 no?
    'r4 = repmat(''a'', 2, 3);', ...                     % 152 no
    'q = quad(''sin'', 0, 1);', ...                      % 153 DQUAD
    'k = logical(1);', ...                               % 154 LOGL
    'k2 = logical(2);', ...                              % 155 no?
    'k3 = logical([1 0]);', ...                          % 156 no?
    'm = ndims(v) == 2;', ...                            % 157 ISMAT
    'm2 = 2 == ndims(v);', ...                           % 158 ISMAT?
    'st = string(''a'');', ...                           % 159 STRQUOT
    'st2 = string(''a'', ''b'');', ...                   % 160 ?
    'st3 = string("a");', ...                            % 161 ?
    'dt = datestr(1);', ...                              % 162 DATST
    'dt2 = x.datestr;', ...                              % 163 no (a field)
    'dt3 = x.datestr(1);', ...                           % 164 no
    'hh = @hist;', ...                                   % 165 HIST?
    'gf = getfield(x, ''a'', {1});', ...                 % 166 GFLD
    'sf = setfield(x, ''a'', 1);', ...                   % 167 SFLD
    'disp({c d e g f n l r r2 r3 r4 q k k2 k3 m m2 st st2 st3 dt dt2 dt3 hh gf sf})', ...% 168
    'end'});
writeFile(fullfile(work, 'c1_cyc.m'), { ...
    'function r = c1_cyc(x)', ...
    'r = 0;', ...
    'if x > 1 && x < 5', ...
    '    r = 1;', ...
    'elseif x > 0 || x == -1', ...
    '    r = 2;', ...
    'end', ...
    'for k = 1:x', ...
    '    r = r + k;', ...
    'end', ...
    'while r > 100', ...
    '    r = r - 1;', ...
    'end', ...
    'switch r', ...
    '    case 1', ...
    '        r = 2;', ...
    '    case {2, 3}', ...
    '        r = 3;', ...
    '    otherwise', ...
    '        r = 4;', ...
    'end', ...
    'try', ...
    '    r = r + 1;', ...
    'catch', ...
    '    r = 0;', ...
    'end', ...
    'end', ...
    '', ...
    'function helper2(a)', ...
    'if a, disp(1), end', ...
    'end'});
writeFile(fullfile(work, 'c2_clean.m'), {'function c2_clean()', 'end'});
writeFile(fullfile(work, 'c3_script.m'), {'x = 1', 'y = [1];', 'disp(y)'});

addpath(work);
cd(work);

files = {'b1_defnu', 'b2_defnu', 'b3_clearload', 'b4_comma', 'b5_stci', 'b6_iscl', 'b7_elarlog', 'b8_eval', 'b9_nasgu', 'c1_cyc', 'c3_script'};
for k = 1:numel(files)
    fprintf('== messages: %s\n', files{k});
    try
        m = checkcode(files{k}, '-id', '-struct');
        printMessages(files{k}, m);
    catch e
        fprintf('ERR|%s|%s\n', e.identifier, short(e.message));
    end
end

fprintf('== the outputs\n');
try
    [m, f] = checkcode('c1_cyc');
    fprintf('two outputs: m %s %s, f %s %s = %s\n', class(m), mat2str(size(m)), class(f), mat2str(size(f)), short(strjoin(f, ';')));
    m = checkcode('c3_script');
    fprintf('fields: %s\n', strjoin(fieldnames(m)', ','));
    fprintf('message %s, line %s %s, column %s %s, fix %s %s\n', class(m(1).message), class(m(1).line), mat2str(m(1).line), class(m(1).column), mat2str(m(1).column), class(m(1).fix), mat2str(m(1).fix));
    m = checkcode('c3_script', '-id');
    fprintf('fields -id: %s\n', strjoin(fieldnames(m)', ','));
    m = checkcode('c2_clean');
    fprintf('clean struct: %s size %s fields %s\n', class(m), mat2str(size(m)), strjoin(fieldnames(m)', ','));
    m = checkcode('c2_clean', '-id');
    fprintf('clean struct -id: %s size %s fields %s\n', class(m), mat2str(size(m)), strjoin(fieldnames(m)', ','));
    m = checkcode({'c1_cyc', 'c2_clean'});
    fprintf('cell struct: %s size %s; {1} %s %s; {2} %s %s\n', class(m), mat2str(size(m)), class(m{1}), mat2str(size(m{1})), class(m{2}), mat2str(size(m{2})));
    [m, f] = checkcode({'c1_cyc', 'c2_clean'});
    fprintf('cell two outputs: f %s %s = %s\n', class(f), mat2str(size(f)), short(strjoin(f, ';')));
    [m, f] = checkcode('c1_cyc', 'c2_clean');
    fprintf('two names two outputs: m %s %s f %s %s\n', class(m), mat2str(size(m)), class(f), mat2str(size(f)));
    m = checkcode({'c1_cyc'});
    fprintf('one-cell struct: %s size %s\n', class(m), mat2str(size(m)));
    m = checkcode({'c1_cyc'; 'c2_clean'});
    fprintf('column cell struct: %s size %s\n', class(m), mat2str(size(m)));
    m = checkcode(["c1_cyc", "c2_clean"]);
    fprintf('string array struct: %s size %s\n', class(m), mat2str(size(m)));
    m = checkcode('c1_cyc', '-cyc', '-id');
    printMessages('c1_cyc -cyc -id', m);
    m = checkcode('c1_cyc', '-modcyc', '-id');
    printMessages('c1_cyc -modcyc -id', m);
    m = checkcode('c1_cyc', '-cyc', '-modcyc', '-id');
    printMessages('c1_cyc -cyc -modcyc -id', m);
    m = checkcode('c3_script', '-cyc', '-id');
    printMessages('c3_script -cyc -id', m);
    s = checkcode({'c1_cyc', 'c2_clean'}, '-string');
    fprintf('multi-file string: %s\n', short(strrep(strrep(s, char(10), '\n'), char(13), '\r')));
    s = checkcode({'c2_clean', 'c1_cyc'}, '-string', '-id');
    fprintf('multi-file string -id: %s\n', short(strrep(strrep(s, char(10), '\n'), char(13), '\r')));
    s = checkcode('c1_cyc', '-string');
    fprintf('one-file string: %s\n', short(strrep(strrep(s, char(10), '\n'), char(13), '\r')));
    s = checkcode('c2_clean', '-string');
    fprintf('clean string: [%s] %s\n', s, mat2str(size(s)));
    s = checkcode(fullfile(work, 'c1_cyc.m'), '-string');
    fprintf('absolute string: %s\n', short(strrep(s, char(10), '\n')));
    s = evalc('checkcode(fullfile(work, ''c1_cyc.m''))');
    fprintf('absolute display: %s\n', short(strrep(s, char(10), '\n')));
    s = evalc('checkcode(fullfile(work, ''c1_cyc.m''), ''-fullpath'')');
    fprintf('absolute -fullpath display: %s\n', short(strrep(s, char(10), '\n')));
    s = evalc('checkcode(''c1_cyc'', ''-fullpath'')');
    fprintf('name -fullpath display: %s\n', short(strrep(s, char(10), '\n')));
    s = evalc('checkcode(''c1_cyc.m'', ''-fullpath'')');
    fprintf('name.m -fullpath display: %s\n', short(strrep(s, char(10), '\n')));
    s = evalc('checkcode(''-text'', sprintf(''function t()\nx = 1;\nend\n''), ''t.m'', ''-id'')');
    fprintf('-text -id display: %s\n', short(strrep(s, char(10), '\n')));
    m = checkcode('-text', sprintf('function t()\nx = 1;\nend\n'), 't.m', '-id');
    printMessages('-text struct', m);
    [m, f] = checkcode('-text', sprintf('function t()\nx = 1;\nend\n'), 't.m');
    fprintf('-text files: %s\n', short(strjoin(f, ';')));
    s = evalc('checkcode(''-text'', sprintf(''function t()\nx = 1;\nend\n''), ''anything.txt'')');
    fprintf('-text .txt display: %s\n', short(strrep(s, char(10), '\n')));
    s = evalc('checkcode(''c1_cyc'', ''-notok'', ''-id'')');
    fprintf('-notok display: %s\n', short(strrep(s, char(10), '\n')));
catch e
    fprintf('ERR|%s|%s\n', e.identifier, short(getReport(e, 'basic')));
end

fprintf('== refusals\n');
refuse('x = checkcode(''c1_cyc'', ''-disp'')', @() tooMany(), short);
refuse('[a,b,c] = checkcode(''c1_cyc'')', @() threeOut(), short);
refuse('checkcode(''c1_cyc'', ''-text'')', @() checkcode('c1_cyc', '-text'), short);
refuse('checkcode(''-text'', 5, ''t.m'')', @() checkcode('-text', 5, 't.m'), short);
refuse('checkcode(''c1_cyc'', 5)', @() checkcode('c1_cyc', 5), short);
refuse('checkcode(''c1_cyc'', '''')', @() checkcode('c1_cyc', ''), short);
refuse('checkcode(''c1_cyc'', ''-id'', ''-string'', ''-struct'')', @() evalc('q = checkcode(''c1_cyc'', ''-id'', ''-string'', ''-struct''); disp(class(q))'), short);
refuse('checkcode(''c1_cyc'', ''-string'', ''-disp'')', @() evalc('checkcode(''c1_cyc'', ''-string'', ''-disp'')'), short);
refuse('codeIssues(''c1_cyc'')', @() issuesNoExt(), short);
refuse('codeIssues(work, IncludeSubfolders=false)', @() issuesFolder(work), short);
refuse('codeIssues(''c1_cyc.m'', ''c2_clean.m'')', @() codeIssues('c1_cyc.m', 'c2_clean.m'), short);
refuse('codeIssues(5)', @() codeIssues(5), short);
fprintf('done\n');

function tooMany()
x = checkcode('c1_cyc', '-disp'); %#ok<NASGU>
end

function threeOut()
[a, b, c] = checkcode('c1_cyc'); %#ok<ASGLU>
end

function r = issuesNoExt()
q = codeIssues('c1_cyc');
r = char(strjoin(q.Files, ';'));
end

function r = issuesFolder(work)
mkdir(fullfile(work, 'sub'));
copyfile(fullfile(work, 'c1_cyc.m'), fullfile(work, 'sub', 'c4_sub.m'));
q = codeIssues(work, 'IncludeSubfolders', false);
q2 = codeIssues(work);
r = sprintf('%d files; with subfolders %d files', numel(q.Files), numel(q2.Files));
end

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

function refuse(label, fn, short)
try
    r = fn();
    if ischar(r), fprintf('ACCEPTED: %s -> %s<EOS>\n', label, short(r)); else, fprintf('ACCEPTED: %s\n', label); end
catch e
    fprintf('REFUSED: %s -> %s: %s\n', label, e.identifier, short(e.message));
end
end
