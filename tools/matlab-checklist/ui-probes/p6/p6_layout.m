function root = p6_layout(tag)
%P6_LAYOUT The layout stage P6's probes measure (packages and class folders plan): rt/<tag>/lib
%holds a +pkg package with a sub-package, a class, an enumeration, a class folder, a script and a
%private folder, and the class folders @Thing (with private/), @Sub < Thing and @HThing; rt/<tag>/cur
%holds one root per dependency question. Fresh each run.
here = fileparts(mfilename('fullpath'));
root = fullfile(here, 'rt', tag);
if exist(root, 'dir'), rmdir(root, 's'); end
cur = fullfile(root, 'cur');
lib = fullfile(root, 'lib');
mkdir(cur); mkdir(lib);

% ---- the package ----
w(lib, '+pkg/f.m', {'function r = f(x)', 'r = x * 10;', 'end'});
w(lib, '+pkg/g2.m', {'function r = g2(x)', 'r = pkg.f(x) + 1;', 'end'});
w(lib, '+pkg/two.m', {'function [a, b] = two(x)', 'a = x;', 'b = -x;', 'end'});
w(lib, '+pkg/q.m', {'function r = q()', 'r = p();', 'end'});
w(lib, '+pkg/private/p.m', {'function r = p()', 'r = ''private p'';', 'end'});
w(lib, '+pkg/s.m', {'disp(''script s ran'');', 'sval = 42;'});
w(lib, '+pkg/max.m', {'function r = max(x)', 'r = ''pkg max'';', 'end'});
w(lib, '+pkg/mk2.m', {'function r = mk2()', 'import pkg.C', 'r = C(2);', 'end'});
w(lib, '+pkg/C.m', {'classdef C', '    properties (Constant)', '        K = 7', '    end', '    properties', '        V = 0', '    end', ...
    '    methods', '        function o = C(v)', '            if nargin, o.V = v; end', '        end', ...
    '        function r = m(o)', '            r = o.V * 2;', '        end', '    end', ...
    '    methods (Static)', '        function r = stat(x)', '            r = x + 100;', '        end', '    end', 'end'});
w(lib, '+pkg/E.m', {'classdef E', '    enumeration', '        A, B', '    end', 'end'});
w(lib, '+pkg/+sub/h.m', {'function r = h(x)', 'r = x - 1;', 'end'});
w(lib, '+pkg/+sub/K.m', {'classdef K < pkg.C', '    methods', '        function r = m(o)', '            r = m@pkg.C(o) + 1;', '        end', '    end', 'end'});
w(lib, '+pkg/@D/D.m', {'classdef D', '    properties', '        V', '    end', '    methods', '        function o = D(v)', '            o.V = v;', '        end', '        r = dm(o)', '    end', 'end'});
w(lib, '+pkg/@D/dm.m', {'function r = dm(o)', 'r = dp(o.V);', 'end'});
w(lib, '+pkg/@D/private/dp.m', {'function r = dp(v)', 'r = v + 1000;', 'end'});
w(lib, '+pkg/@D/unused.m', {'function r = unused(o)', 'r = o.V;', 'end'});
w(lib, '+other/f.m', {'function r = f(x)', 'r = x * 1000;', 'end'});

% ---- the class folders ----
w(lib, '@Thing/Thing.m', {'classdef Thing', '    properties', '        W = 0', '    end', '    methods', ...
    '        function o = Thing(w)', '            if nargin, o.W = w; end', '        end', '        r = extent(o)', '    end', ...
    '    methods (Static)', '        r = staticOne(x)', '    end', 'end'});
w(lib, '@Thing/extent.m', {'function r = extent(o)', 'r = o.W * o.W;', 'end'});
w(lib, '@Thing/scale.m', {'function r = scale(o, k)', '% not declared in the classdef', 'r = hidden(o.W) * k;', 'end'});
w(lib, '@Thing/private/hidden.m', {'function r = hidden(w)', 'r = w + 0.5;', 'end'});
w(lib, '@Thing/staticOne.m', {'function r = staticOne(x)', 'r = x * 3;', 'end'});
w(lib, '@Thing/usesfar.m', {'function r = usesfar(o)', 'r = farhelper(o.W);', 'end'});
w(lib, 'farhelper.m', {'function r = farhelper(w)', 'r = w * 11;', 'end'});
w(lib, 'unusedlib.m', {'function r = unusedlib(w)', 'r = w;', 'end'});
w(lib, '@Sub/Sub.m', {'classdef Sub < Thing', '    methods', '        function o = Sub(w)', '            o@Thing(w);', '        end', '    end', 'end'});
w(lib, '@Sub/extent.m', {'function r = extent(o)', 'r = -1;', 'end'});
w(lib, '@HThing/HThing.m', {'classdef HThing < handle', '    properties', '        N = 0', '    end', 'end'});
w(lib, '@HThing/bump.m', {'function bump(o)', 'o.N = o.N + 1;', 'end'});

% ---- the roots, one per question ----
w(cur, 'usepkg.m', {'function usepkg()', 'beep(''off'');', 'fprintf(''f\t%g\n'', pkg.f(2));', 't = Thing(3);', 'fprintf(''extent\t%g\n'', t.extent());', ...
    'fprintf(''scale\t%g\n'', scale(t, 2));', 'fprintf(''deployed\t%d\n'', isdeployed);', 'end'});
w(cur, 'useclass.m', {'function r = useclass()', 'r = pkg.C(3).m();', 'end'});
w(cur, 'usesubclass.m', {'function r = usesubclass()', 'r = pkg.sub.K(1).m();', 'end'});
w(cur, 'usefolderclass.m', {'function r = usefolderclass()', 'r = pkg.D(1).dm();', 'end'});
w(cur, 'usesub.m', {'function r = usesub()', 'r = pkg.sub.h(5);', 'end'});
w(cur, 'usescript.m', {'function r = usescript()', 'pkg.s', 'r = sval;', 'end'});
w(cur, 'usesubfolder.m', {'function r = usesubfolder()', 'r = Sub(2).extent();', 'end'});
w(cur, 'usehandle.m', {'function r = usehandle()', 'h = @pkg.f;', 'g = @Thing.staticOne;', 'r = h(2) + g(1);', 'end'});
w(cur, 'usemeta.m', {'function r = usemeta()', 'r = {?pkg.C, ?Thing};', 'end'});
w(cur, 'usestatic.m', {'function r = usestatic()', 'r = pkg.C.K + pkg.C.stat(1);', 'end'});
w(cur, 'useenum.m', {'function r = useenum()', 'r = pkg.E.A;', 'end'});
w(cur, 'useimport.m', {'function r = useimport()', 'import pkg.f', 'r = f(2);', 'end'});
w(cur, 'useimportclass.m', {'function r = useimportclass()', 'import pkg.C', 'r = C(1).m();', 'end'});
w(cur, 'useimportsub.m', {'function r = useimportsub()', 'import pkg.sub.*', 'r = h(1);', 'end'});
w(cur, 'usewild.m', {'function r = usewild()', 'import pkg.*', 'r = f(1) + C(1).m() + two(1);', 'end'});
w(cur, 'usefeval.m', {'function r = usefeval()', 'r = feval(''pkg.f'', 2);', 'end'});
w(cur, 'usepragma.m', {'function r = usepragma()', '%#function pkg.f', 'r = 1;', 'end'});
w(cur, 'usepragmaclass.m', {'function r = usepragmaclass()', '%#function Thing', 'r = 1;', 'end'});
w(cur, 'usemax.m', {'function r = usemax()', 'r = pkg.max(1);', 'end'});
w(cur, 'usedotvar.m', {'function r = usedotvar()', 'pkg = struct(''f'', 1);', 'r = pkg.f;', 'end'});
w(cur, 'usenosuch.m', {'function r = usenosuch()', 'r = pkg.nosuch(1);', 'end'});
w(cur, 'usechain.m', {'function r = usechain()', 'r = pkg.g2(1);', 'end'});
w(cur, 'useprivate.m', {'function r = useprivate()', 'r = pkg.q();', 'end'});
w(cur, 'usehandleobj.m', {'function r = usehandleobj()', 'o = HThing;', 'o.bump();', 'r = o.N;', 'end'});
w(cur, 'usemethodhandle.m', {'function r = usemethodhandle()', 'h = @extent;', 'r = h(Thing(2));', 'end'});
w(cur, 'usebarestatic.m', {'function r = usebarestatic()', 'r = Thing.staticOne(2);', 'end'});
w(cur, 'usetwo.m', {'function r = usetwo()', 'r = pkg.f(1) + other.f(1);', 'end'});
w(cur, 'useonlything.m', {'function r = useonlything()', 'r = Thing(2).W;', 'end'});
w(cur, 'usepkgscriptroot.m', {'pkg.f(1);', 'Thing(1);'});
end

function w(root, relative, lines)
file = fullfile(root, relative);
folder = fileparts(file);
if ~exist(folder, 'dir'), mkdir(folder); end
fid = fopen(file, 'w');
fprintf(fid, '%s\n', lines{:});
fclose(fid);
end
