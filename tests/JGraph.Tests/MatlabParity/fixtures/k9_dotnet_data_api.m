% record: -noFigureWindows
% Compiler and SDK plan, stage K9: .NET assemblies for the MATLAB Data API - compiler.build.dotNETAssembly
% and mcc -W dotnet:...,api=matlab-data, the archive beside the assembly, the strongly typed interface
% each function and class gets (its text after the header), the note when none can be made, and what
% was found on the way: keyHash, configureDictionary, isobject of the built-in classes, isscalar of a
% table, and switch on a string. The assemblies are built and never loaded here.
beep('off');
root = tempname;
src = fullfile(root, 'src');
mkdir(src);
put(src, 'k9fa.m', {'function r = k9fa(x, y)', '%K9FA A typed function with an optional input.', 'arguments', ...
    '    x (1,1) double {mustBeReal}', '    y (1,:) char = ''a''', 'end', 'r = [num2str(x) y];', 'end'});
put(src, 'k9fm.m', {'function [total, label] = k9fm(values)', '%K9FM Two typed outputs.', 'arguments (Input)', ...
    '    values (1,:) double {mustBeReal}', 'end', 'arguments (Output)', '    total (1,1) double {mustBeReal}', ...
    '    label (1,1) string', 'end', 'total = sum(values);', 'label = "n";', 'end'});
put(src, 'k9fn.m', {'function r = k9fn(x, opts)', '%K9FN A name-value argument.', 'arguments', '    x (1,1) double', ...
    '    opts.Scale (1,1) double = 1', 'end', 'r = x * opts.Scale;', 'end'});
put(src, 'k9fv.m', {'function r = k9fv(varargin)', '%K9FV Any number of inputs.', 'r = nargin;', 'end'});
put(src, 'k9pt.m', {'classdef k9pt', '    properties', '        X (1,1) double {mustBeReal} = 0', '    end', ...
    '    properties (SetAccess = private)', '        Tag = ''pt''', '    end', '    methods', '        function obj = k9pt(x)', ...
    '            obj.X = x;', '        end', '        function d = twice(obj)', '            d = 2 * obj.X;', '        end', '    end', ...
    '    methods (Static)', '        function p = zero()', '            p = k9pt(0);', '        end', '    end', 'end'});
put(src, 'k9en.m', {'classdef k9en', '    enumeration', '        low', '        high', '    end', 'end'});
put(src, 'k9hc.m', {'classdef k9hc < handle', '    properties', '        Count = 0', '    end', '    methods', ...
    '        function add(obj, n)', '            arguments', '                obj', '                n (1,1) double {mustBeReal}', ...
    '            end', '            obj.Count = obj.Count + n;', '        end', '    end', 'end'});
put(src, 'k9ho.m', {'classdef k9ho < handle', '    properties', '        Count = 0', '    end', '    methods', ...
    '        function add(obj, n)', '            arguments', '                obj', '                n (1,1) double {mustBeReal} = 1', ...
    '            end', '            obj.Count = obj.Count + n;', '        end', '    end', 'end'});
old = cd(src);

% compiler.build with the Data API: the files, the interface's files, and the text of each.
d = fullfile(root, 'api');
lastwarn('', '');
r = compiler.build.dotNETAssembly({'k9fa.m', 'k9fm.m', 'k9pt.m', 'k9en.m', 'k9hc.m'}, 'Interface', 'matlab-data', ...
    'AssemblyName', 'K9F', 'OutputDir', d);
[~, id] = lastwarn;
u2_chk('api_warn', @() id);
u2_chk('api_type', @() r.BuildType);
u2_chk('api_files', @() rel(r.Files, d));
u2_chk('api_written', @() names(d));
u2_chk('api_typed', @() names(fullfile(d, 'strongly_typed_interface')));
u2_chk('api_k9fa', @() body(d, 'k9fa.cs'));
u2_chk('api_k9fm', @() body(d, 'k9fm.cs'));
u2_chk('api_k9pt', @() body(d, 'k9pt.cs'));
u2_chk('api_k9en', @() body(d, 'k9en.cs'));
u2_chk('api_k9hc', @() body(d, 'k9hc.cs'));
u2_chk('api_header', @() header(d, 'k9en.cs'));

% A function with varargin: the archive alone, and the note that says so.
d = fullfile(root, 'var');
t = evalc('r = compiler.build.dotNETAssembly(''k9fv.m'', ''Interface'', ''matlab-data'', ''AssemblyName'', ''K9V'', ''OutputDir'', d, ''EmbedArchive'', ''off'');');
u2_chk('var_note', @() contains(t, 'Note: A strongly typed interface for the MATLAB code could not be generated'));
u2_chk('var_files', @() rel(r.Files, d));
u2_chk('var_written', @() names(d));

% STRONGLY_TYPED_DISABLED: the archive alone, without the note.
setenv('STRONGLY_TYPED_DISABLED', '1');
d = fullfile(root, 'off');
t = evalc('r = compiler.build.dotNETAssembly(''k9fa.m'', ''Interface'', ''matlab-data'', ''AssemblyName'', ''K9O'', ''OutputDir'', d, ''EmbedArchive'', ''off'');');
setenv('STRONGLY_TYPED_DISABLED', '');
u2_chk('off_note', @() contains(t, 'Note:'));
u2_chk('off_files', @() rel(r.Files, d));

% mcc -W dotnet:...,api=matlab-data, and -v's count of what the interface holds.
u2_chk('mcc_min', @() build(root, 'mcc_min', {'-W', 'dotnet:K9M,api=matlab-data', '-T', 'link:lib', 'k9fa.m', 'k9en.m'}));
u2_chk('mcc_separate', @() build(root, 'mcc_separate', {'-W', 'dotnet:K9S,api=matlab-data', '-T', 'link:lib', '-C', 'k9fa.m'}));
u2_chk('mcc_var', @() build(root, 'mcc_var', {'-W', 'dotnet:K9W,api=matlab-data', '-T', 'link:lib', 'k9fv.m'}));
t = evalc('mcc(''-W'', ''dotnet:K9Y,api=matlab-data'', ''-T'', ''link:lib'', ''-v'', ''-d'', fullfile(root, ''mcc_verbose''), ''k9fa.m'', ''k9pt.m'');');
u2_chk('mcc_verbose_count', @() regexprep(countline(t), '[^ ]*strongly_typed_interface', '<folder>'));

% Two of R2025b's generator defects, corrected: name-value arguments passed by name, and an
% optional input of a method passed without a dangling comma (R2025b's did not compile).
d = fullfile(root, 'nv');
r = compiler.build.dotNETAssembly('k9fn.m', 'Interface', 'matlab-data', 'AssemblyName', 'K9N', 'OutputDir', d, 'EmbedArchive', 'off');
chkdiv('nv_k9fn', @() body(d, 'k9fn.cs'), 'ADR0265');
d = fullfile(root, 'opt');
t = evalc('r = compiler.build.dotNETAssembly(''k9ho.m'', ''Interface'', ''matlab-data'', ''AssemblyName'', ''K9H'', ''OutputDir'', d, ''EmbedArchive'', ''off'');');
chkdiv('opt_files', @() rel(r.Files, d), 'ADR0265');
chkdiv('opt_k9ho', @() body(d, 'k9ho.cs'), 'ADR0265');

% Found on the way.
u2_chk('keyhash_class', @() class(keyHash(5)));
u2_chk('keyhash_equal', @() [keyHash(5) == keyHash(5), keyHash('ab') == keyHash('ab'), keyHash(5) == keyHash(6), keyHash({1, 'a'}) == keyHash({1, 'a'})]);
c = configureDictionary("string", "double");
u2_chk('configured', @() [isConfigured(c), numEntries(c)]);
u2_chk('configured_keys', @() class(keys(c)));
u2_chk('configured_insert', @() numEntries(insert(c, "a", 1)));
u2_chk('isobject_builtin', @() [isobject(table([1; 2])), isobject(datetime(2026, 1, 1)), isobject(seconds(3)), isobject("s"), ...
    isobject(timetable(seconds(1), 1)), isobject(calendarDuration(1, 2, 3)), isobject(@sin), isobject(struct('a', 1)), isobject({1})]);
u2_chk('isscalar_table', @() [isscalar(table([1; 2], [3; 4])), isscalar(table(1))]);
u2_chk('switch_string', @() pick("row"));
u2_chk('switch_char', @() pick('column'));
u2_chk('switch_cell', @() pick("both"));
cd(old);
rmdir(root, 's');

function r = pick(kind)
switch kind
    case 'row', r = 1;
    case "column", r = 2;
    case {'both', 'neither'}, r = 3;
    otherwise, r = 0;
end
end

function s = body(folder, file)
% A generated file after its header: the lines past the usings, which name each product's namespaces.
lines = splitlines(fileread(fullfile(folder, 'strongly_typed_interface', file)));
s = strjoin(lines(9:end)', ' / ');
end

function s = header(folder, file)
% The header's lines that do not name the product: the file and the frame around it.
lines = splitlines(fileread(fullfile(folder, 'strongly_typed_interface', file)));
s = strjoin([lines(1:2); lines(5:6)]', ' / ');
end

function s = countline(text)
lines = splitlines(text);
s = lines{find(contains(lines, 'class(es) and'), 1)};
end

function s = build(root, name, args)
% A build's outcome: ok or its refusal, the warning it raised, and the files it wrote.
folder = fullfile(root, name);
lastwarn('', '');
s = err(@() quiet(args, folder));
[~, id] = lastwarn;
s = sprintf('%s | warn %s | files %s | typed %s', s, id, names(folder), names(fullfile(folder, 'strongly_typed_interface')));
end

function quiet(args, folder)
% mcc with what it prints kept out of the fixture's output: evalc sees this function's workspace.
evalc('mcc(args{:}, ''-d'', folder)');
end

function s = err(f)
try
    f();
    s = 'no error';
catch e
    s = [e.identifier ' / ' strrep(e.message, newline, ' / ')];
end
end

function s = names(folder)
if ~exist(folder, 'dir')
    s = '';
    return
end
d = dir(folder);
d = d(~[d.isdir]);
s = strjoin(sort({d.name}), ' ');
end

function s = rel(files, folder)
files = cellfun(@(f) strrep(f, [folder filesep], ''), cellstr(files), 'UniformOutput', false);
s = strjoin(files(:)', ' ');
end

function put(folder, name, lines)
fid = fopen(fullfile(folder, name), 'w');
fprintf(fid, '%s\n', lines{:});
fclose(fid);
end

function chkdiv(name, fn, adr)
% A u2_chk line whose rule is a recorded divergence: the stamp adds what JGraph prints.
fprintf('CHK|%s|%s|div=%s\n', name, u2_text(fn()), adr);
end
