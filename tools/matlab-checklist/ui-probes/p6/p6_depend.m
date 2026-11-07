function p6_depend(part)
if nargin < 1, part = ''; end
%P6_DEPEND What R2025b's dependency analysis takes for a root that reaches +package members and
%@Class folders (packages and class folders plan, stage P6): matlab.codetools.requiredFilesAndProducts
%over p6_layout's roots, then mcc -m of usepkg.m - the archive's entries and its manifest's folders -
%and the built program run. One key<TAB>value line per answer; paths relative to the layout as <T>.
here = fileparts(mfilename('fullpath'));
beep('off');
root = p6_layout('depend');
cur = fullfile(root, 'cur');
lib = fullfile(root, 'lib');
old = cd(cur);
cleanup = onCleanup(@() cd(old));
addpath(lib);

if isempty(part) || strcmp(part, 'rfp')
roots = {'usepkg', 'useclass', 'usesubclass', 'usefolderclass', 'usesub', 'usescript', 'usesubfolder', 'usehandle', ...
    'usemeta', 'usestatic', 'useenum', 'useimport', 'useimportclass', 'useimportsub', 'usewild', 'usefeval', 'usepragma', ...
    'usepragmaclass', 'usemax', 'usedotvar', 'usenosuch', 'usechain', 'useprivate', 'usehandleobj', 'usemethodhandle', ...
    'usebarestatic', 'usetwo', 'useonlything', 'usepkgscriptroot'};
for k = 1:numel(roots)
    rfp(root, ['rfp.' roots{k}], [roots{k} '.m']);
end
% The member files and folders themselves as roots.
rfp(root, 'rfp.member', fullfile(lib, '+pkg', 'g2.m'));
rfp(root, 'rfp.classfile', fullfile(lib, '@Thing', 'Thing.m'));
rfp(root, 'rfp.methodfile', fullfile(lib, '@Thing', 'scale.m'));
rfp(root, 'rfp.pkgfolder', fullfile(lib, '+pkg'));
rfp(root, 'rfp.classfolder', fullfile(lib, '@Thing'));
rfp(root, 'rfp.dotted', 'pkg.f');
rfp(root, 'rfp.dottedclass', 'pkg.C');
rfp(root, 'rfp.slash', 'Thing/extent');
rfp(root, 'rfp.toponly', 'usepkg.m', 'toponly');
% Without the library on the path: nothing reached.
rmpath(lib);
rfp(root, 'rfp.usepkg.nolib', 'usepkg.m');
addpath(lib);
end
if ~isempty(part) && ~strcmp(part, 'mcc'), return; end

% ---- mcc -m usepkg.m: the archive and the program ----
out = fullfile(root, 'build');
mkdir(out);
try
    txt = quietMcc({'-m', 'usepkg.m', '-C', '-d', out});
    fprintf('mcc.usepkg\tok\n');
    lines = splitlines(string(txt));
    for j = 1:numel(lines)
        if contains(lines(j), 'Warning') || contains(lines(j), 'warning'), fprintf('mcc.usepkg.warn\t%s\n', rel(char(lines(j)), root)); end
    end
catch ME
    fprintf('mcc.usepkg\tERR %s | %s\n', ME.identifier, flat(ME.message));
end
listing('mcc.usepkg', out, root);
exe = fullfile(out, 'usepkg.exe');
if exist(exe, 'file')
    runprog('run.usepkg', exe, root, fullfile(root, 'runhere'));
end

% ---- mcc -m of a main inside the package, and a class folder's method as main ----
out2 = fullfile(root, 'build2');
mkdir(out2);
try
    quietMcc({'-m', fullfile(lib, '+pkg', 'g2.m'), '-C', '-d', out2});
    fprintf('mcc.pkgmain\tok\n');
catch ME
    fprintf('mcc.pkgmain\tERR %s | %s\n', ME.identifier, flat(ME.message));
end
listing('mcc.pkgmain', out2, root);
exe2 = fullfile(out2, 'g2.exe');
if exist(exe2, 'file')
    runprog('run.pkgmain', exe2, root, fullfile(root, 'runhere2'), '3');
end
end

function rfp(root, key, varargin)
lastwarn('', '');
try
    [f, p] = matlab.codetools.requiredFilesAndProducts(varargin{:});
    fprintf('%s.files\t%s\n', key, strjoin(cellfun(@(x) rel(x, root), sort(f), 'UniformOutput', false), ' ; '));
    fprintf('%s.products\t%s\n', key, strjoin({p.Name}, ','));
catch ME
    fprintf('%s\tERR %s | %s\n', key, ME.identifier, flat(ME.message));
end
[msg, id] = lastwarn;
if ~isempty(msg), fprintf('%s.warn\t%s | %s\n', key, id, flat(msg)); end
end

function txt = quietMcc(args)
% evalc in a named function: a lambda's workspace holds only what its body names.
txt = evalc('mcc(args{:})');
end

function listing(key, d, root)
% The archive's entries under fsroot/ and the manifest's own folders.
ctf = dir(fullfile(d, '*.ctf'));
if isempty(ctf), fprintf('%s.ctf\tnone\n', key); return; end
tmp = fullfile(root, 'unz');
if exist(tmp, 'dir'), rmdir(tmp, 's'); end
got = unzip(fullfile(d, ctf(1).name), tmp);
got = sort(cellfun(@(g) strrep(g(numel(tmp) + 2:end), '\', '/'), got, 'UniformOutput', false));
keep = got(startsWith(got, 'fsroot/') & ~startsWith(got, 'fsroot/.matlab/'));
fprintf('%s.ctf.n\t%d\n', key, numel(keep));
for j = 1:numel(keep), fprintf('%s.ctf[%d]\t%s\n', key, j, rel(keep{j}, root)); end
man = fullfile(tmp, '.META', 'manifest.xml');
if exist(man, 'file')
    m = fileread(man);
    tok = regexp(m, '<directory id="[^"]*" path="([^"$][^"]*)"', 'tokens');
    tok = [tok{:}];
    fprintf('%s.manifest.dirs\t%s\n', key, rel(strjoin(tok, ' | '), root));
    tok = regexp(m, '<function[^>]*>\s*<name>([^<]*)</name>', 'tokens');
    fprintf('%s.manifest.functions\t%s\n', key, strjoin([[tok{:}] {''}], ' | '));
    mainref = regexp(m, '<main-function ref="([^"]*)"', 'tokens', 'once');
    fprintf('%s.manifest.main\t%s\n', key, strjoin([mainref {''}], ''));
end
rmdir(tmp, 's');
end

function runprog(key, exe, root, folder, varargin)
% The built program run from its own folder with the runtime on the PATH; its lines indented.
if ~exist(folder, 'dir'), mkdir(folder); end
oldPath = getenv('PATH');
setenv('PATH', [fullfile(matlabroot, 'runtime', 'win64') ';' fullfile(matlabroot, 'bin', 'win64') ';' oldPath]);
c = onCleanup(@() setenv('PATH', oldPath));
old = cd(folder);
c2 = onCleanup(@() cd(old));
args = strjoin(varargin, ' ');
[status, out] = system(['"' exe '" ' args]);
fprintf('%s.exit\t%d\n', key, status);
lines = splitlines(string(out));
for j = 1:numel(lines)
    if strlength(lines(j)) > 0, fprintf('%s.out\t%s\n', key, rel(char(lines(j)), root)); end
end
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
