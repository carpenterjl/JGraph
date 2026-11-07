function p7_build(work)
%P7_BUILD mcc -m of the acceptance library's program p7_tool.m (packages and class folders plan,
%stage P7) under <work>\build, in either engine: the build's outcome, then the archive's entries
%and its manifest's folders, functions and main. The program itself is run by accept-p7.ps1.
beep('off');
here = fileparts(mfilename('fullpath'));
lib = fullfile(here, 'lib');
addpath(lib);
out = fullfile(work, 'build');
if exist(out, 'dir'), rmdir(out, 's'); end
mkdir(out);
old = cd(here);
cleanup = onCleanup(@() cd(old));
try
    txt = evalc('mcc(''-m'', ''p7_tool.m'', ''-C'', ''-d'', out)');
    fprintf('build\tok\n');
    lines = splitlines(string(txt));
    for j = 1:numel(lines)
        if contains(lower(lines(j)), 'warning'), fprintf('build.warn\t%s\n', char(lines(j))); end
    end
catch ME
    fprintf('build\tERR %s | %s\n', ME.identifier, strrep(char(ME.message), newline, ' / '));
end
files = dir(out);
names = sort({files(~[files.isdir]).name});
keep = names(endsWith(names, {'.exe', '.ctf'}));
fprintf('deliverables\t%s\n', strjoin(keep, ' | '));

% The archive's entries under fsroot/ and the manifest's own folders, the probe's folder shortened.
ctf = dir(fullfile(out, '*.ctf'));
if isempty(ctf), fprintf('archive\tnone\n'); return; end
tmp = fullfile(work, 'unz');
if exist(tmp, 'dir'), rmdir(tmp, 's'); end
got = unzip(fullfile(out, ctf(1).name), tmp);
got = sort(cellfun(@(g) strrep(g(numel(tmp) + 2:end), '\', '/'), got, 'UniformOutput', false));
keep = got(startsWith(got, 'fsroot/') & ~startsWith(got, 'fsroot/.matlab/'));
fprintf('archive.n\t%d\n', numel(keep));
for j = 1:numel(keep), fprintf('archive[%d]\t%s\n', j, short(keep{j}, here)); end
man = fullfile(tmp, '.META', 'manifest.xml');
if exist(man, 'file')
    m = fileread(man);
    tok = regexp(m, '<directory id="[^"]*" path="([^"$][^"]*)"', 'tokens');
    tok = [tok{:}];
    fprintf('manifest.dirs\t%s\n', short(strjoin(tok, ' | '), here));
    tok = regexp(m, '<function[^>]*>\s*<name>([^<]*)</name>', 'tokens');
    fprintf('manifest.functions\t%s\n', strjoin([[tok{:}] {''}], ' | '));
    mainref = regexp(m, '<main-function ref="([^"]*)"', 'tokens', 'once');
    fprintf('manifest.main\t%s\n', strjoin([mainref {''}], ''));
end
rmdir(tmp, 's');
end

function s = short(p, here)
% The archive writes a path without its drive and with %20 for a space; the probe's folder, in
% that form, becomes <P7>.
p = strrep(p, '%20', ' ');
h = strrep(here, '\', '/');
s = strrep(p, h(3:end), '/<P7>');
s = strrep(s, h, '<P7>');
end
