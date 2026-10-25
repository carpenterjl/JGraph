% record: -noFigureWindows
% Compiler and SDK plan, stage K9: what an arguments block says when a value does not fit - the size,
% class and validator refusals in R2025b's words for a positional input, the conversions it makes on
% the way in, and how a missing, extra or defaulted input is answered. Each function is written to a
% scratch folder and called there.
beep('off');
root = tempname;
mkdir(root);
old = cd(root);

sdecl = {'s11', '(1,1)'; 's1n', '(1,:)'; 'sn1', '(:,1)'; 's13', '(1,3)'; 's23', '(2,3)'; 'snn', '(:,:)'; ...
    's2n', '(2,:)'; 'sn3', '(:,3)'; 's123', '(1,2,3)'};
for k = 1:size(sdecl, 1)
    put(root, [sdecl{k, 1} '.m'], {['function r = ' sdecl{k, 1} '(x)'], 'arguments', ['    x ' sdecl{k, 2}], 'end', 'r = k9d(x);', 'end'});
end
cdecl = {'cdouble', 'double'; 'cint8', 'int8'; 'clogical', 'logical'; 'cchar', 'char'; 'cstring', 'string'; ...
    'ccell', 'cell'; 'cstruct', 'struct'; 'chandle', 'function_handle'; 'csingle', 'single'; 'cuint16', 'uint16'; ...
    'creal', 'double {mustBeReal}'; 'cscalardouble', '(1,1) double'};
for k = 1:size(cdecl, 1)
    put(root, [cdecl{k, 1} '.m'], {['function r = ' cdecl{k, 1} '(x)'], 'arguments', ['    x ' cdecl{k, 2}], 'end', 'r = k9d(x);', 'end'});
end
put(root, 'k9d.m', {'function r = k9d(x)', ...
    'if isnumeric(x) || islogical(x) || ischar(x), v = mat2str(x); elseif isstring(x), v = char(strjoin(x, ''+'')); else, v = ''-''; end', ...
    'r = sprintf(''%s %s %s'', class(x), mat2str(size(x)), v);', 'end'});
put(root, 'va.m', {'function r = va(a, b, c)', 'arguments', '    a (1,1) double {mustBePositive}', ...
    '    b (1,:) double {mustBeInteger, mustBeNonnegative} = 1', '    c (1,1) string {mustBeMember(c, ["on" "off"])} = "on"', 'end', ...
    'r = sprintf(''%g|%s|%s'', a, mat2str(b), c);', 'end'});
put(root, 'df.m', {'function r = df(a, b)', 'arguments', '    a', '    b (1,1) double = [1 2]', 'end', 'r = b;', 'end'});
put(root, 'cu.m', {'function r = cu(x)', 'arguments', '    x {mustBeA(x, "double"), mustBeVector}', 'end', 'r = numel(x);', 'end'});
put(root, 'tw.m', {'function r = tw(a, b)', 'arguments', '    a (1,1) double', '    b (1,1) double', 'end', 'r = a + b;', 'end'});

% Sizes.
sizes = {1, [1 2 3], [1; 2; 3], [1 2; 3 4], ones(2, 3), ones(3, 2), ones(2, 2, 2), [], zeros(0, 3), 'abc'};
names = {'scalar', 'row', 'column', 'square', 'two_by_three', 'three_by_two', 'cube', 'empty', 'empty03', 'chars'};
decl = {'1x1', '1xn', 'nx1', '1x3', '2x3', 'nxn', '2xn', 'nx3', '1x2x3'};
for k = 1:size(sdecl, 1)
    f = str2func(sdecl{k, 1});
    for j = 1:numel(sizes)
        u2_chk(sprintf('size_%s_%s', decl{k}, names{j}), @() f(sizes{j}));
    end
end

% Classes.
values = {1, 2.5, 300, -1, true, 'A', "7", "x", {1}, struct('a', 1), @sin, 1+2i, NaN, int8(5), uint8(250), single(1.5), [1 2]};
vnames = {'one', 'half', 'big', 'negative', 'true', 'char', 'string7', 'stringx', 'cell', 'struct', 'handle', 'complex', 'nan', 'int8', 'uint8', 'single', 'pair'};
cnames = {'double', 'int8', 'logical', 'char', 'string', 'cell', 'struct', 'function_handle', 'single', 'uint16', 'real', 'scalardouble'};
for k = 1:size(cdecl, 1)
    f = str2func(cdecl{k, 1});
    for j = 1:numel(values)
        key = sprintf('class_%s_%s', cnames{k}, vnames{j});
        if any(strcmp(cnames{k}, {'int8', 'uint16'})) && strcmp(vnames{j}, 'complex')
            divchk(key, @() f(values{j}), 'ADR0265'); % JGraph keeps no complex integers
        else
            u2_chk(key, @() f(values{j}));
        end
    end
end

% Validators, defaults, counts.
u2_chk('valid_ok', @() va(2));
u2_chk('valid_positive', @() va(-2));
u2_chk('valid_second', @() va(2, 1.5));
u2_chk('valid_second_negative', @() va(2, -1));
u2_chk('valid_member', @() va(2, 1, "maybe"));
u2_chk('valid_member_char', @() va(2, 1, 'off'));
u2_chk('default_invalid', @() df(1));
u2_chk('default_given', @() df(1, 5));
u2_chk('custom_mustBeA', @() cu('abc'));
u2_chk('custom_vector', @() cu(ones(2)));
u2_chk('count_missing_one', @() tw(1));
u2_chk('count_missing_two', @() tw());
u2_chk('count_extra', @() tw(1, 2, 3));
u2_chk('count_nargin', @() nargin('tw'));

cd(old);
rmdir(root, 's');

function divchk(name, fn, adr)
% A u2_chk line whose rule is a recorded divergence, the error's identifier standing for the value.
try
    v = u2_text(fn());
catch err
    v = err.identifier;
end
fprintf('CHK|%s|%s|div=%s\n', name, v, adr);
end

function put(folder, name, lines)
fid = fopen(fullfile(folder, name), 'w');
fprintf(fid, '%s\n', lines{:});
fclose(fid);
end
