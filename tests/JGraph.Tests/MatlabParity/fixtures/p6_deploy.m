% p6_deploy.m -- the packages and class folders plan's stage P6 (ADR 0312): what the dependency
% analysis (matlab.codetools.requiredFilesAndProducts) takes for a root that reaches +package
% members and @Class folders -- a package member alone, a class folder whole with its private/ and
% its unused method files, a dotted chain's longest member, a class file's dotted superclass, a
% subclass folder with its superclass's, handles, metaclasses, statics, enumerations, explicit and
% wildcard imports, pragmas, a package's private folder, a bare method name, a script root, the
% member files and the folders themselves as roots, the dotted root forms, toponly, and nothing
% without the library on the path. Every rule is R2025b's as recorded here; the probe
% tools/matlab-checklist/ui-probes/p6/p6_depend settled them first.
%
% A fixture is one file, so this one builds the folders it needs under tempdir -- a current folder
% holding one root per question and a library holding the package and the class folders -- and
% removes them at the end. Paths are shown relative to that root.

start = pwd;
root = fullfile(tempdir, 'p6_deploy');
if exist(root, 'dir') == 7
    rmdir(root, 's');
end
cur = fullfile(root, 'cur');
lib = fullfile(root, 'lib');
mkdir(cur);
mkdir(lib);
warning('off', 'backtrace');

% ---- the package ----
put(fullfile(lib, '+p6pkg', 'f.m'), {'function r = f(x)', 'r = x * 10;', 'end'});
put(fullfile(lib, '+p6pkg', 'g2.m'), {'function r = g2(x)', 'r = p6pkg.f(x) + 1;', 'end'});
put(fullfile(lib, '+p6pkg', 'two.m'), {'function [a, b] = two(x)', 'a = x;', 'b = -x;', 'end'});
put(fullfile(lib, '+p6pkg', 'q.m'), {'function r = q()', 'r = p();', 'end'});
put(fullfile(lib, '+p6pkg', 'private', 'p.m'), {'function r = p()', 'r = ''private p'';', 'end'});
put(fullfile(lib, '+p6pkg', 's.m'), {'disp(''script s ran'');', 'sval = 42;'});
put(fullfile(lib, '+p6pkg', 'max.m'), {'function r = max(x)', 'r = ''pkg max'';', 'end'});
put(fullfile(lib, '+p6pkg', 'mk2.m'), {'function r = mk2()', 'import p6pkg.C', 'r = C(2);', 'end'});
put(fullfile(lib, '+p6pkg', 'C.m'), {'classdef C', '    properties (Constant)', '        K = 7', '    end', '    properties', '        V = 0', '    end', ...
    '    methods', '        function o = C(v)', '            if nargin, o.V = v; end', '        end', ...
    '        function r = m(o)', '            r = o.V * 2;', '        end', '    end', ...
    '    methods (Static)', '        function r = stat(x)', '            r = x + 100;', '        end', '    end', 'end'});
put(fullfile(lib, '+p6pkg', 'E.m'), {'classdef E', '    enumeration', '        A, B', '    end', 'end'});
put(fullfile(lib, '+p6pkg', '+sub', 'h.m'), {'function r = h(x)', 'r = x - 1;', 'end'});
put(fullfile(lib, '+p6pkg', '+sub', 'K.m'), {'classdef K < p6pkg.C', '    methods', '        function r = m(o)', '            r = m@p6pkg.C(o) + 1;', '        end', '    end', 'end'});
put(fullfile(lib, '+p6pkg', '@D', 'D.m'), {'classdef D', '    properties', '        V', '    end', '    methods', '        function o = D(v)', '            o.V = v;', '        end', '        r = dm(o)', '    end', 'end'});
put(fullfile(lib, '+p6pkg', '@D', 'dm.m'), {'function r = dm(o)', 'r = dp(o.V);', 'end'});
put(fullfile(lib, '+p6pkg', '@D', 'private', 'dp.m'), {'function r = dp(v)', 'r = v + 1000;', 'end'});
put(fullfile(lib, '+p6pkg', '@D', 'unused.m'), {'function r = unused(o)', 'r = o.V;', 'end'});
put(fullfile(lib, '+p6other', 'f.m'), {'function r = f(x)', 'r = x * 1000;', 'end'});

% ---- the class folders ----
put(fullfile(lib, '@P6Thing', 'P6Thing.m'), {'classdef P6Thing', '    properties', '        W = 0', '    end', '    methods', ...
    '        function o = P6Thing(w)', '            if nargin, o.W = w; end', '        end', '        r = extent(o)', '    end', ...
    '    methods (Static)', '        r = staticOne(x)', '    end', 'end'});
put(fullfile(lib, '@P6Thing', 'extent.m'), {'function r = extent(o)', 'r = o.W * o.W;', 'end'});
put(fullfile(lib, '@P6Thing', 'scale.m'), {'function r = scale(o, k)', '% not declared in the classdef', 'r = hidden(o.W) * k;', 'end'});
put(fullfile(lib, '@P6Thing', 'private', 'hidden.m'), {'function r = hidden(w)', 'r = w + 0.5;', 'end'});
put(fullfile(lib, '@P6Thing', 'staticOne.m'), {'function r = staticOne(x)', 'r = x * 3;', 'end'});
put(fullfile(lib, '@P6Thing', 'usesfar.m'), {'function r = usesfar(o)', 'r = p6farhelper(o.W);', 'end'});
put(fullfile(lib, 'p6farhelper.m'), {'function r = p6farhelper(w)', 'r = w * 11;', 'end'});
put(fullfile(lib, 'p6unusedlib.m'), {'function r = p6unusedlib(w)', 'r = w;', 'end'});
put(fullfile(lib, '@P6Sub', 'P6Sub.m'), {'classdef P6Sub < P6Thing', '    methods', '        function o = P6Sub(w)', '            o@P6Thing(w);', '        end', '    end', 'end'});
put(fullfile(lib, '@P6Sub', 'extent.m'), {'function r = extent(o)', 'r = -1;', 'end'});
put(fullfile(lib, '@P6HThing', 'P6HThing.m'), {'classdef P6HThing < handle', '    properties', '        N = 0', '    end', 'end'});
put(fullfile(lib, '@P6HThing', 'bump.m'), {'function bump(o)', 'o.N = o.N + 1;', 'end'});

% ---- the roots, one per question ----
put(fullfile(cur, 'usepkg.m'), {'function usepkg()', 'fprintf(''f\t%g\n'', p6pkg.f(2));', 't = P6Thing(3);', 'fprintf(''extent\t%g\n'', t.extent());', ...
    'fprintf(''scale\t%g\n'', scale(t, 2));', 'end'});
put(fullfile(cur, 'useclass.m'), {'function r = useclass()', 'r = p6pkg.C(3).m();', 'end'});
put(fullfile(cur, 'usesubclass.m'), {'function r = usesubclass()', 'r = p6pkg.sub.K(1).m();', 'end'});
put(fullfile(cur, 'usefolderclass.m'), {'function r = usefolderclass()', 'r = p6pkg.D(1).dm();', 'end'});
put(fullfile(cur, 'usesub.m'), {'function r = usesub()', 'r = p6pkg.sub.h(5);', 'end'});
put(fullfile(cur, 'usescript.m'), {'function r = usescript()', 'p6pkg.s', 'r = sval;', 'end'});
put(fullfile(cur, 'usesubfolder.m'), {'function r = usesubfolder()', 'r = P6Sub(2).extent();', 'end'});
put(fullfile(cur, 'usehandle.m'), {'function r = usehandle()', 'h = @p6pkg.f;', 'g = @P6Thing.staticOne;', 'r = h(2) + g(1);', 'end'});
put(fullfile(cur, 'usemeta.m'), {'function r = usemeta()', 'r = {?p6pkg.C, ?P6Thing};', 'end'});
put(fullfile(cur, 'usestatic.m'), {'function r = usestatic()', 'r = p6pkg.C.K + p6pkg.C.stat(1);', 'end'});
put(fullfile(cur, 'useenum.m'), {'function r = useenum()', 'r = p6pkg.E.A;', 'end'});
put(fullfile(cur, 'useimport.m'), {'function r = useimport()', 'import p6pkg.f', 'r = f(2);', 'end'});
put(fullfile(cur, 'useimportclass.m'), {'function r = useimportclass()', 'import p6pkg.C', 'r = C(1).m();', 'end'});
put(fullfile(cur, 'useimportsub.m'), {'function r = useimportsub()', 'import p6pkg.sub.*', 'r = h(1);', 'end'});
put(fullfile(cur, 'usewild.m'), {'function r = usewild()', 'import p6pkg.*', 'r = f(1) + C(1).m() + two(1);', 'end'});
put(fullfile(cur, 'usefeval.m'), {'function r = usefeval()', 'r = feval(''p6pkg.f'', 2);', 'end'});
put(fullfile(cur, 'usepragma.m'), {'function r = usepragma()', '%#function p6pkg.f', 'r = 1;', 'end'});
put(fullfile(cur, 'usepragmaclass.m'), {'function r = usepragmaclass()', '%#function P6Thing', 'r = 1;', 'end'});
put(fullfile(cur, 'usemax.m'), {'function r = usemax()', 'r = p6pkg.max(1);', 'end'});
put(fullfile(cur, 'usedotvar.m'), {'function r = usedotvar()', 'p6pkg = struct(''f'', 1);', 'r = p6pkg.f;', 'end'});
put(fullfile(cur, 'usenosuch.m'), {'function r = usenosuch()', 'r = p6pkg.nosuch(1);', 'end'});
put(fullfile(cur, 'usechain.m'), {'function r = usechain()', 'r = p6pkg.g2(1);', 'end'});
put(fullfile(cur, 'useprivate.m'), {'function r = useprivate()', 'r = p6pkg.q();', 'end'});
put(fullfile(cur, 'usehandleobj.m'), {'function r = usehandleobj()', 'o = P6HThing;', 'o.bump();', 'r = o.N;', 'end'});
put(fullfile(cur, 'usemethodhandle.m'), {'function r = usemethodhandle()', 'h = @extent;', 'r = h(P6Thing(2));', 'end'});
put(fullfile(cur, 'usebarestatic.m'), {'function r = usebarestatic()', 'r = P6Thing.staticOne(2);', 'end'});
put(fullfile(cur, 'usetwo.m'), {'function r = usetwo()', 'r = p6pkg.f(1) + p6other.f(1);', 'end'});
put(fullfile(cur, 'useonlything.m'), {'function r = useonlything()', 'r = P6Thing(2).W;', 'end'});
put(fullfile(cur, 'usepkgscriptroot.m'), {'p6pkg.f(1);', 'P6Thing(1);'});

cd(cur);
addpath(lib);
rel = @(p) strrep(strrep(p, root, '<root>'), '\', '/');
files = @(varargin) taken(rel, varargin{:});

% ---- one root per question ----
chk('rfp_usepkg', files('usepkg.m'));
chk('rfp_useclass', files('useclass.m'));
chk('rfp_usesubclass', files('usesubclass.m'));
chk('rfp_usefolderclass', files('usefolderclass.m'));
chk('rfp_usesub', files('usesub.m'));
chk('rfp_usescript', files('usescript.m'));
chk('rfp_usesubfolder', files('usesubfolder.m'));
chk('rfp_usehandle', files('usehandle.m'));
chk('rfp_usemeta', files('usemeta.m'));
chk('rfp_usestatic', files('usestatic.m'));
chk('rfp_useenum', files('useenum.m'));
chk('rfp_useimport', files('useimport.m'));
chk('rfp_useimportclass', files('useimportclass.m'));
chk('rfp_useimportsub', files('useimportsub.m'));
chk('rfp_usewild', files('usewild.m'));
chk('rfp_usefeval', files('usefeval.m'));
chk('rfp_usepragma', files('usepragma.m'));
chk('rfp_usepragmaclass', files('usepragmaclass.m'));
chk('rfp_usemax', files('usemax.m'));
chk('rfp_usedotvar', files('usedotvar.m'));
chk('rfp_usenosuch', files('usenosuch.m'));
chk('rfp_usechain', files('usechain.m'));
chk('rfp_useprivate', files('useprivate.m'));
chk('rfp_usehandleobj', files('usehandleobj.m'));
chk('rfp_usemethodhandle', files('usemethodhandle.m'));
chk('rfp_usebarestatic', files('usebarestatic.m'));
chk('rfp_usetwo', files('usetwo.m'));
chk('rfp_useonlything', files('useonlything.m'));
chk('rfp_usepkgscriptroot', files('usepkgscriptroot.m'));

% ---- the member files and the folders themselves as roots, the dotted root forms ----
chk('rfp_member', files(fullfile(lib, '+p6pkg', 'g2.m')));
chk('rfp_classfile', files(fullfile(lib, '@P6Thing', 'P6Thing.m')));
chk('rfp_pkgfolder', files(fullfile(lib, '+p6pkg')));
chk('rfp_classfolder', files(fullfile(lib, '@P6Thing')));
chk('rfp_dotted', files('p6pkg.f'));
chk('rfp_dottedclass', files('p6pkg.C'));
chk('rfp_toponly', files('usepkg.m', 'toponly'));
chk('rfp_products', strjoin(products('usepkg.m'), ','));

% ---- without the library on the path: nothing reached ----
rmpath(lib);
chk('rfp_usepkg_nolib', files('usepkg.m'));
addpath(lib);

cd(start);
rmpath(lib);
rmdir(root, 's');

function put(path, lines)
folder = fileparts(path);
if ~isempty(folder) && exist(folder, 'dir') ~= 7
    mkdir(folder);
end
fid = fopen(path, 'w');
for k = 1:length(lines)
    fprintf(fid, '%s\n', lines{k});
end
fclose(fid);
end

function s = taken(rel, varargin)
f = matlab.codetools.requiredFilesAndProducts(varargin{:});
s = strjoin(cellfun(rel, sort(f), 'UniformOutput', false), ' ; ');
end

function names = products(varargin)
[~, p] = matlab.codetools.requiredFilesAndProducts(varargin{:});
names = {p.Name};
end

function chk(name, v)
fprintf('CHK|%s|%s|exact\n', name, show(v));
end

function s = show(v)
if ischar(v)
    s = v;
elseif isstring(v)
    s = char(v);
elseif isscalar(v) && (isnumeric(v) || islogical(v))
    s = sprintf('%.17g', double(v));
else
    s = ['<' class(v) '>'];
end
end
