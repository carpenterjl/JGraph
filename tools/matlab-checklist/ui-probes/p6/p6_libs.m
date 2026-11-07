function p6_libs()
%P6_LIBS What R2025b's mcc makes of a +package function for the other targets (packages and class
%folders plan, stage P6): the .NET Data API archive (the typed interface's file, stage K9 measured
%one already), a C shared library (the header's names), a Python package (the __init__ lines
%naming the function) and a Production Server archive (its discovery file), each from
%lib/+pkg/f.m and lib/+pkg/g2.m, and a class folder's class file for the C library. Builds only;
%nothing built is run. One key<TAB>value line per answer.
beep('off');
root = p6_layout('libs');
lib = fullfile(root, 'lib');
old = cd(fullfile(root, 'cur'));
cleanup = onCleanup(@() cd(old));
addpath(lib);
f = fullfile(lib, '+pkg', 'f.m');
g2 = fullfile(lib, '+pkg', 'g2.m');

d = fullfile(root, 'clib');
build('c', @() mcc('-W', 'lib:p6clib', '-T', 'link:lib', '-d', d, f, g2), d);
show('c.header', fullfile(d, 'p6clib.h'), {'EXTERN_C', 'mlx', 'mlf', 'pkg'}, root);

d = fullfile(root, 'cthing');
build('cthing', @() mcc('-W', 'lib:p6cthing', '-T', 'link:lib', '-d', d, fullfile(lib, '@Thing', 'staticOne.m')), d);
show('cthing.header', fullfile(d, 'p6cthing.h'), {'EXTERN_C', 'mlx', 'mlf', 'static'}, root);

d = fullfile(root, 'py');
build('python', @() mcc('-W', 'python:p6py', '-d', d, f, g2), d);
show('python.init', fullfile(d, 'p6py', '__init__.py'), {'pkg', 'f(', 'g2'}, root);

d = fullfile(root, 'mps');
build('mps', @() mcc('-W', 'CTF:p6mps', '-U', '-d', d, f, g2), d);
listing('mps', d, root);

d = fullfile(root, 'net');
build('net', @() mcc('-W', 'dotnet:P6Net,api=matlab-data', '-T', 'link:lib', '-d', d, f, g2), d);
files = dir(fullfile(d, 'strongly_typed_interface'));
fprintf('net.typed\t%s\n', strjoin(sort({files(~[files.isdir]).name}), ' | '));
listing('net', d, root);
end

function build(key, fn, d)
if ~exist(d, 'dir'), mkdir(d); end
try
    evalc('fn()');
    fprintf('build.%s\tok\n', key);
catch ME
    fprintf('build.%s\tERR %s | %s\n', key, ME.identifier, flat(ME.message));
end
files = dir(d);
fprintf('files.%s\t%s\n', key, strjoin(sort({files(~[files.isdir]).name}), ' | '));
end

function show(key, file, words, root)
if ~exist(file, 'file'), fprintf('%s\tmissing\n', key); return; end
lines = splitlines(string(fileread(file)));
for j = 1:numel(lines)
    l = char(lines(j));
    if any(cellfun(@(w) contains(l, w), words)) && ~startsWith(strtrim(l), '*') && ~startsWith(strtrim(l), '#')
        fprintf('%s\t%s\n', key, rel(strtrim(l), root));
    end
end
end

function listing(key, d, root)
ctf = dir(fullfile(d, '*.ctf'));
if isempty(ctf), fprintf('%s.ctf\tnone\n', key); return; end
tmp = fullfile(root, 'unz');
if exist(tmp, 'dir'), rmdir(tmp, 's'); end
got = unzip(fullfile(d, ctf(1).name), tmp);
got = sort(cellfun(@(g) strrep(g(numel(tmp) + 2:end), '\', '/'), got, 'UniformOutput', false));
keep = got(startsWith(got, 'fsroot/') & ~startsWith(got, 'fsroot/.matlab/'));
fprintf('%s.ctf\t%s\n', key, rel(strjoin(keep, ' | '), root));
man = fullfile(tmp, '.META', 'manifest.xml');
if exist(man, 'file')
    m = fileread(man);
    tok = regexp(m, '<directory id="[^"]*" path="([^"$][^"]*)"', 'tokens');
    tok = [tok{:}];
    fprintf('%s.manifest.dirs\t%s\n', key, rel(strjoin(tok, ' | '), root));
    tok = regexp(m, '<function[^>]*>\s*<name>([^<]*)</name>', 'tokens');
    fprintf('%s.manifest.functions\t%s\n', key, strjoin([[tok{:}] {''}], ' | '));
end
disc = dir(fullfile(tmp, '**', '*discovery*'));
for j = 1:numel(disc)
    t = fileread(fullfile(disc(j).folder, disc(j).name));
    fprintf('%s.discovery\t%s\n', key, regexprep(t, '\s+', ' '));
end
rmdir(tmp, 's');
end

function s = rel(s, root)
s = strrep(s, root, '<T>');
s = strrep(s, strrep(root, '\', '/'), '<T>');
s = strrep(s, matlabroot, '<MATLABROOT>');
end

function s = flat(s)
s = regexprep(char(s), '<a [^>]*>|</a>', '');
s = strrep(s, sprintf('\r'), '');
s = strrep(s, newline, ' / ');
end
