function probe_k2_depend(outRoot, part)
%PROBE_K2_DEPEND What R2025b's dependency analysis finds (compiler and SDK plan, stage K2).
%   probe_k2_depend(outRoot) writes k2_make_tree's tree under outRoot\k2tree, then records
%   matlab.codetools.requiredFilesAndProducts over it ('rfp') and what mcc -m -C puts in the
%   archive, the log files it writes and the warnings it raises ('mcc'). part names one of the two.
%   Nothing built here is run. One key<TAB>value line per answer; a list prints its count, then
%   one key[i] line per entry, with paths relative to the tree as <T>\...
if nargin < 2, part = ''; end
here = fileparts(mfilename('fullpath'));
addpath(fullfile(here, 'src'));
beep('off');
root = k2_make_tree(fullfile(outRoot, 'k2tree'));
app = fullfile(root, 'app');
lib = fullfile(root, 'lib');
old = cd(app);
cleanup = onCleanup(@() cd(old));

if isempty(part) || strcmp(part, 'rfp')
    addpath(lib);
    rfp(root, 'rfp.main', 'k2_main.m');
    rfp(root, 'rfp.main.toponly', 'k2_main.m', 'toponly');
    rfp(root, 'rfp.noext', 'k2_helper');
    rfp(root, 'rfp.full', fullfile(app, 'k2_helper.m'));
    rfp(root, 'rfp.two', {'k2_helper.m', 'k2_script.m'});
    rfp(root, 'rfp.string', "k2_helper.m");
    rfp(root, 'rfp.stringarray', ["k2_helper.m", "k2_script.m"]);
    rfp(root, 'rfp.class', 'k2_cls.m');
    rfp(root, 'rfp.script', 'k2_script.m');
    rfp(root, 'rfp.private', fullfile(app, 'private', 'k2_priv.m'));
    rfp(root, 'rfp.data', 'k2_notes.txt');
    rfp(root, 'rfp.mat', 'k2_data.mat');
    rfp(root, 'rfp.fig', 'k2_main.fig');
    rfp(root, 'rfp.missing', 'k2_nosuch.m');
    rfp(root, 'rfp.folder', app);
    rfp(root, 'rfp.number', 5);
    rfp(root, 'rfp.empty', {});
    rfp(root, 'rfp.bad.option', 'k2_helper.m', 'bogus');
    rfp(root, 'rfp.builtin', 'ones');
    rfp(root, 'rfp.toolboxfile', 'butter');
    k0_try('rfp.nargout3', @() threeOut());
    k0_try('rfp.noargs', @() matlab.codetools.requiredFilesAndProducts());
    rmpath(lib);
    rfp(root, 'rfp.main.nolib', 'k2_main.m');
    k0_try('dsp.main', @() compiler.codetools.deployableSupportPackages('k2_main.m'));
    k0_try('dsp.two', @() compiler.codetools.deployableSupportPackages({'k2_main.m', 'k2_helper.m'}));
    k0_try('dsp.missing', @() compiler.codetools.deployableSupportPackages('k2_nosuch.m'));
end

if isempty(part) || strcmp(part, 'mcc')
    pack = fullfile(root, 'pack');
    builds = {
        'mcc.plain',     {'-m', 'k2_main.m'}, true
        'mcc.nolib',     {'-m', 'k2_main.m'}, false
        'mcc.X',         {'-m', 'k2_main.m', '-X'}, true
        'mcc.I',         {'-m', 'k2_main.m', '-I', lib}, false
        'mcc.a',         {'-m', 'k2_main.m', '-a', pack, '-a', 'k2_excl.m', '-a', fullfile(lib, 'k2_libdeep.m'), '-a', 'k2_unnamed.txt'}, true
        'mcc.a.wild',    {'-m', 'k2_main.m', '-a', fullfile(pack, '*.txt')}, true
        'mcc.dup',       {'-m', 'k2_main.m', '-a', fullfile(root, 'other', 'k2_helper.m')}, true
        'mcc.N',         {'-m', 'k2_main.m', '-N'}, true
        'mcc.N.p',       {'-m', 'k2_main.m', '-N', '-p', 'signal'}, true
        'mcc.script',    {'-m', 'k2_helper.m', '-a', 'k2_script.m'}, true
        };
    for k = 1:size(builds, 1)
        [key, args, withLib] = builds{k, :};
        if withLib, addpath(lib); else, rmpath(lib); end
        d = fullfile(outRoot, 'k2out', matlab.lang.makeValidName(key));
        if exist(d, 'dir'), rmdir(d, 's'); end
        mkdir(d);
        lastwarn('', '');
        try
            txt = quietMcc([args, {'-C', '-d', d}]);
            fprintf('%s\tok\n', key);
        catch ME
            txt = '';
            fprintf('%s\tERR %s | %s\n', key, ME.identifier, k0_flat(ME.message));
        end
        lines = splitlines(strtrim(txt));
        lines = lines(strlength(lines) > 0);
        fprintf('%s.output.n\t%d\n', key, numel(lines));
        for j = 1:numel(lines), fprintf('%s.output[%d]\t%s\n', key, j, rel(lines{j}, root, outRoot)); end
        k0_warned(key);
        listing(key, d, root, outRoot);
    end
    if exist(lib, 'dir'), rmpath(lib); end
end
end

function rfp(root, key, varargin)
lastwarn('', '');
try
    [f, p] = matlab.codetools.requiredFilesAndProducts(varargin{:});
    fprintf('%s.files\t%s %s\n', key, class(f), mat2str(size(f)));
    for j = 1:numel(f), fprintf('%s.files[%d]\t%s\n', key, j, rel(f{j}, root, '')); end
    fprintf('%s.products\t%s %s fields [%s]\n', key, class(p), mat2str(size(p)), strjoin(fieldnames(p)', ', '));
    for j = 1:numel(p)
        fprintf('%s.products[%d]\t%s | %s | %d | %d\n', key, j, p(j).Name, p(j).Version, p(j).ProductNumber, p(j).Certain);
    end
catch ME
    fprintf('%s\tERR %s | %s\n', key, ME.identifier, k0_flat(ME.message));
end
k0_warned(key);
end

function v = threeOut()
[a, b, c] = matlab.codetools.requiredFilesAndProducts('k2_helper.m'); %#ok<ASGLU>
v = {class(c), size(c)};
end

function txt = quietMcc(args)
% evalc in a named function: a lambda's workspace holds only what its body names.
txt = evalc('mcc(args{:})');
end

function listing(key, d, root, outRoot)
% The archive's entries, the files beside it and the log files' text.
files = dir(d);
names = sort({files(~[files.isdir]).name});
fprintf('%s.out\t%s\n', key, strjoin(names, ' '));
ctf = dir(fullfile(d, '*.ctf'));
if ~isempty(ctf)
    tmp = fullfile(outRoot, 'k2unz');
    if exist(tmp, 'dir'), rmdir(tmp, 's'); end
    got = unzip(fullfile(d, ctf(1).name), tmp);
    got = sort(cellfun(@(g) strrep(g(numel(tmp) + 2:end), '\', '/'), got, 'UniformOutput', false));
    keep = got(startsWith(got, 'fsroot/') & ~startsWith(got, 'fsroot/.matlab/'));
    fprintf('%s.ctf.n\t%d\n', key, numel(keep));
    for j = 1:numel(keep), fprintf('%s.ctf[%d]\t%s\n', key, j, keep{j}); end
    man = fullfile(tmp, '.META', 'manifest.xml');
    if exist(man, 'file')
        m = fileread(man);
        % The archive's own folders; the $MCRROOT_MCR ones are the runtime's toolbox path.
        tok = regexp(m, '<directory id="[^"]*" path="([^"$][^"]*)"', 'tokens');
        tok = [tok{:}];
        fprintf('%s.manifest.dirs\t%s\n', key, strjoin(tok, ' | '));
        fprintf('%s.manifest.runtimedirs\t%d\n', key, numel(regexp(m, 'path="\$MCRROOT', 'start')));
    end
    rmdir(tmp, 's');
end
for n = {'mccExcludedFiles.log', 'unresolvedSymbols.txt', 'requiredMCRProducts.txt', ...
        'includedSupportPackages.txt', 'buildresult.json'}
    p = fullfile(d, n{1});
    if exist(p, 'file')
        t = splitlines(string(fileread(p)));
        fprintf('%s.%s.n\t%d\n', key, n{1}, numel(t));
        for j = 1:numel(t), fprintf('%s.%s[%d]\t%s\n', key, n{1}, j, rel(char(t(j)), root, outRoot)); end
    end
end
end

function s = rel(s, root, outRoot)
s = strrep(s, root, '<T>');
s = strrep(s, strrep(root, '\', '/'), '<T>');
if ~isempty(outRoot)
    s = strrep(s, outRoot, '<OUT>');
    s = strrep(s, strrep(outRoot, '\', '/'), '<OUT>');
end
s = strrep(s, matlabroot, '<MATLABROOT>');
s = strrep(s, sprintf('\t'), ' <TAB> ');
end
