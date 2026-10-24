function probe_k2_rules(outRoot)
%PROBE_K2_RULES The detail of R2025b's dependency rules, one small main per question (compiler and
%SDK plan, stage K2): which literal file names the data rule takes, the pragmas' forms, calls
%through a dot, handles and nested functions. For each case prints
%   case<TAB>the files matlab.codetools.requiredFilesAndProducts answers, relative to the folder
% (the case's own file left out), or ERR identifier | message.
beep('off');
here = fileparts(mfilename('fullpath'));
addpath(fullfile(here, 'src'));
root = fullfile(outRoot, 'k2rules');
if exist(root, 'dir'), rmdir(root, 's'); end
app = fullfile(root, 'app');
lib = fullfile(root, 'lib');
mkdir(app); mkdir(fullfile(app, 'sub')); mkdir(lib);
for f = ["d.txt", "d.csv", "d.json", "d.dat", "noext", "sub/s.csv", "two words.txt"]
    writelines("1,2", fullfile(app, f));
end
writelines("1,2", fullfile(lib, 'libdata.csv'));
note = 1; %#ok<NASGU>
save(fullfile(app, 'd.mat'), 'note');
save(fullfile(app, 'onlymat.mat'), 'note');
save(fullfile(app, 'lonefig.fig'), '-mat', 'note');
for n = ["fa", "fb", "fc", "fd", "fe", "ff", "fg", "fh", "fi", "fj", "fk", "fl", "fm", "fn"]
    writelines(["function varargout = " + n + "(varargin)", "varargout = {1};", "end"], fullfile(app, n + ".m"));
end
addpath(lib);
old = cd(app);
cleanup = onCleanup(@() cd(old));

cases = {
    'data.sub.slash',     "x = readtable('sub/s.csv');"
    'data.sub.back',      "x = readtable('sub\s.csv');"
    'data.absolute',      "x = readtable('" + fullfile(app, 'd.csv') + "');"
    'data.onpath',        "x = readtable('libdata.csv');"
    'data.notfound.here', "x = readtable('s.csv');"
    'data.second.arg',    "x = readmatrix('d.csv', 'Range', 'A1');"
    'data.cmd.load',      "load d.mat"
    'data.cmd.noext',     "load onlymat"
    'data.fn.noext',      "x = load('onlymat');"
    'data.noext.file',    "x = fileread('noext');"
    'data.json',          "x = fileread('d.json');"
    'data.fopen.mode',    "f = fopen('d.dat', 'r');"
    'data.importdata',    "x = importdata('d.txt');"
    'data.type',          "type d.txt"
    'data.open',          "open('d.txt')"
    'data.variable',      "fn = 'd.txt'; x = fileread(fn);"
    'data.concat',        "x = fileread(['d' '.txt']);"
    'data.string.concat', "x = fileread(""d"" + "".txt"");"
    'data.notlisted',     "x = exist('d.txt'); y = dir('d.csv'); writematrix(1, 'd.csv');"
    'data.space',         "x = fileread('two words.txt');"
    'data.fig.alone',     "x = openfig('lonefig.fig');"
    'data.mat.matfile',   "m = matfile('d.mat');"
    'data.dotted.call',   "x = matlab.io.datastore.TabularTextDatastore('d.csv');"
    'pragma.two',         "%#function fa fb"
    'pragma.comma',       "%#function fa, fb"
    'pragma.ext',         "%#function fa.m"
    'pragma.data',        "%#function d.txt"
    'pragma.indent',      "    %#function fc"
    'pragma.trailing',    "x = 1; %#function fd"
    'pragma.in.sub',      ["x = 1;", "end", "function sub1", "%#function fe", "end"]
    'pragma.space',       "% #function ff"
    'pragma.exclude.ext', ["%#exclude fg.m", "fg(1);"]
    'pragma.exclude.two', ["%#exclude fg fh", "fg(1); fh(1);"]
    'pragma.exclude.data',["%#exclude d.txt", "x = fileread('d.txt');"]
    'call.dot.method',    "o = struct(); o.fi(1);"
    'call.dot.field',     "o = struct('a', 1); x = o.fi;"
    'call.handle.anon',   "h = @() fj(1);"
    'call.nested',        ["x = 1;", "function inner", "fk(1);", "end"]
    'call.cellfun.str',   "c = cellfun('fl', {1});"
    'call.eval',          "eval('fm(1)');"
    'call.var.later',     "fn(1); fn = 3;"
    'call.cmd.args',      "fa hello world"
    'call.in.string',     "s = ""fb"";"
    'call.uppercase',     "FC(1);"
    'call.struct.field.call', "s.fd = @fe; s.fd(1);"
    'data.var.unused',    "fn = 'd.txt'; x = fileread('d.json');"
    'data.literal.noio',  "fn = 'd.txt'; y = numel(fn);"
    'data.arg.second',    "x = readtable(1, 'd.csv');"
    'data.var.reassigned',"fn = 'd.txt'; fn = 'd.json'; x = fileread(fn);"
    'data.var.string',    "fn = ""d.txt""; x = fileread(fn);"
    'data.cell.literal',  "c = {'d.txt'}; x = fileread(c{1});"
    'data.nested.literal',"x = fileread(fa('d.txt'));"
    'data.var.notfound',  "fn = 'nosuch.txt'; x = fileread(fn);"
    'data.code.literal',  "x = fileread('fb.m');"
    'call.global',        "global fa; fa(1);"
    'call.loopvar',       "for fb = 1:3, end; fb(1);"
    'call.input.arg',     "@fc"
    'script.var',         "fa = 1; fa(1);"
    'script.call',        "fb(1);"
    'script.pragma',      "%#function fc"
    'class.default',      ["classdef @NAME", "properties", "P = fa(1);", "end", "end"]
    'class.method',       ["classdef @NAME", "methods", "function m(o)", "fb(1);", "end", "end", "end"]
    'class.super',        ["classdef @NAME < superk", "end"]
    'class.static',       ["classdef @NAME", "methods (Static)", "function s", "fc(1);", "end", "end", "end"]
    };
writelines(["classdef superk < handle", "methods", "function o = superk", "fd(1);", "end", "end", "end"], ...
    fullfile(app, 'superk.m'));
for k = 1:size(cases, 1)
    [key, body] = cases{k, :};
    name = "k2r_" + regexprep(key, '\W', '_');
    nested = any(startsWith(strtrim(body), "end"));
    if startsWith(key, 'script.')
        lines = body;
    elseif startsWith(key, 'class.')
        lines = strrep(body, "@NAME", name);
    elseif strcmp(key, 'call.input.arg')
        lines = ["function " + name + "(fc)", "fc(1);", "end"];
    elseif any(contains(body, "function inner"))
        lines = ["function " + name, body, "end"];
    elseif nested
        lines = ["function " + name, body];
    else
        lines = ["function " + name, body, "end"];
    end
    file = fullfile(app, name + ".m");
    writelines(lines, file);
    lastwarn('', '');
    try
        f = matlab.codetools.requiredFilesAndProducts(char(file));
        f = f(~strcmp(f, file));
        f = cellfun(@(p) strrep(strrep(p, [app filesep], ''), [root filesep], '<T>\'), f, 'UniformOutput', false);
        fprintf('%s\t%s\n', key, strjoin(f, ' | '));
    catch ME
        fprintf('%s\tERR %s | %s\n', key, ME.identifier, k0_flat(ME.message));
    end
    k0_warned(key);
end
rmpath(lib);
end
