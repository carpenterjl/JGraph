function probe_k11_names(work)
%PROBE_K11_NAMES How R2025b's mcc -W lib names a wrapper's arguments and lays its lines out (compiler
%and SDK plan, stage K11): ignored inputs, arguments named as C or C++ words or as the wrappers' own
%parameters, an output and an input of one name, and functions whose names are long enough to
%break the generated lines at different places. The function files are written here; each build is
%-T codegen, so nothing is compiled, and prints its generated wrappers. Run under matlab -batch
%-noFigureWindows (record-k11.ps1) or jgraph -batch (run-k11.ps1). One key<TAB>value line per
%question, a file's lines indented under its key.
beep('off');
here = fileparts(mfilename('fullpath'));
addpath(fullfile(here, 'src'));
if exist(work, 'dir'), rmdir(work, 's'); end
mkdir(work);
old = cd(work);
cleanup = onCleanup(@() cd(old));

files = {
    'k11t2',      '[y] = k11t2(~, ~, c)',        'y = c;'
    'k11t3',      '[a, b] = k11t3(~, x, ~)',     'a = x; b = x;'
    'k11cpp',     '[new, this] = k11cpp(class, delete, template, bool, true, namespace, operator, private)', 'new = 1; this = 2;'
    'k11cw1',     '[auto, register] = k11cw1(signed, unsigned, short, float, void, const)', 'auto = 1; register = 2;'
    'k11cw2',     '[static, extern] = k11cw2(union, enum, goto, typedef, sizeof, volatile)', 'static = 1; extern = 2;'
    'k11cw3',     '[default, inline] = k11cw3(restrict, NULL, mxArray, size_t, main, MW_CALL_CONV)', 'default = 1; inline = 2;'
    'k11cw4',     '[nlhs, plhs] = k11cw4(nrhs, prhs, mclFeval, mclMlfFeval, x_in1, varargin)', 'nlhs = 1; plhs = 2;'
    'k11same2',   '[a, b] = k11same2(b, a)',     'a = b;'
    'k11same3',   '[x, y] = k11same3(x, y, z)',  'x = z;'
    'k11same4',   '[x] = k11same4(x, x_in1)',    'x = x_in1;'
    'k11nargin',  '[varargout] = k11nargin(nargin, nargout)', 'varargout = {};'
    };
for k = 1:size(files, 1)
    writeFunction(files{k, 1}, files{k, 2}, files{k, 3});
end

% Names of each length from 48 to 60, for the line breaks in a wrapper's body and signature.
long = {};
for n = 48:60
    name = ['k11w' sprintf('%02d', n) repmat('x', 1, n - 6)];
    writeFunction(name, sprintf('[out] = %s(in)', name), 'out = in;');
    long{end + 1} = [name '.m']; %#ok<AGROW>
end

% Each odd signature in a build of its own, so a refusal of one leaves the others.
builds = cell(0, 2);
for k = 1:size(files, 1)
    builds(end + 1, :) = {files{k, 1}, {'-W', ['lib:' files{k, 1} 'lib'], '-T', 'codegen', [files{k, 1} '.m']}}; %#ok<AGROW>
end
builds = [builds; {
    'long',   [{'-W', 'lib:k11long', '-T', 'codegen'}, long]
    'o_W',    {'-o', 'k11o', '-W', 'lib:k11w', '-T', 'codegen', 'k11t2.m'}
    'W_W',    {'-W', 'lib:k11a', '-W', 'lib:k11b', '-T', 'codegen', 'k11t2.m'}
    'W_extra', {'-W', 'lib:k11x,bogus', '-T', 'codegen', 'k11t2.m'}
    'W_ver_extra', {'-W', 'lib:k11y,version=2.1,more', '-T', 'codegen', 'k11t2.m'}
    'l_W',    {'-l', '-W', 'lib:k11lw', '-T', 'codegen', 'k11t2.m'}
    'W_l',    {'-W', 'lib:k11wl', '-l', '-T', 'codegen', 'k11t2.m'}
    'm_W',    {'-m', '-W', 'lib:k11mw', '-T', 'codegen', 'k11t2.m'}
    'longlib', {'-W', 'lib:k11_a_library_name_long_enough_to_break_the_general_functions', '-T', 'codegen', 'k11t2.m'}
    }];
for k = 1:size(builds, 1)
    [key, args] = builds{k, :};
    d = fullfile(work, key);
    mkdir(d);
    lastwarn('', '');
    try
        evalc('mcc(args{:}, ''-d'', d)');
        fprintf('%s\tok\n', key);
    catch ME
        fprintf('%s\tERR %s | %s\n', key, ME.identifier, k0_flat(ME.message));
    end
    k0_warned(key);
    listing = dir(d);
    names = sort({listing(~[listing.isdir]).name});
    fprintf('%s.files\t%s\n', key, strjoin(names, ' | '));
    for f = 1:numel(names)
        [~, ~, ext] = fileparts(names{f});
        if any(strcmp(ext, {'.h', '.c'}))
            printWrappers([key '.' names{f}], fullfile(d, names{f}), strcmp(key, 'longlib'));
        end
    end
end
end

function writeFunction(name, signature, body)
fid = fopen([name '.m'], 'w');
fprintf(fid, 'function %s\n%s\nend\n', signature, body);
fclose(fid);
end

% A generated file's lines from its first wrapper on (from its first library function for a library
% whose own name is long), the header and the fixed text before them left out.
function printWrappers(key, file, whole)
text = strrep(fileread(file), sprintf('\r\n'), newline);
lines = splitlines(text);
if whole
    first = find(contains(lines, 'GENERAL LIBRARY FUNCTIONS -- START') | contains(lines, '#define EXPORTING_'), 1);
else
    first = find(contains(lines, 'MLX WRAPPERS FOR USER-DEFINED MATLAB FUNCTIONS -- START') | contains(lines, 'MW_CALL_CONV mlx'), 1);
    if ~isempty(first) && contains(lines{first}, 'MW_CALL_CONV mlx'), first = first - 1; end
end
if isempty(first), first = 1; end
fprintf('%s\t%d lines\n', key, numel(lines));
for j = first:numel(lines)
    fprintf('  |%s\n', lines{j});
end
end
