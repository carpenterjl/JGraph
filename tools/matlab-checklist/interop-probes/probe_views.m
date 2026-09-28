% probe_views: methodsview and libfunctionsview (interop plan, stage 10) without their window:
% methodsview's internal 'noUI' form (its headers and rows, which are what the window shows), the
% two-output methods -full it is built on, and every refusal raised before a window would open.
% Nothing here opens a window: each UI call below fails before methodsViewTable is reached.
dotnetenv("core", Version="8");
a = ip_assets();
addpath(a.here); addpath(a.root);
asm = NET.addAssembly(a.assembly);
lib = 'jgtestlib';
loadlibrary(a.lib, @jgtestlib_proto);

% ---- refusals (no window: each fails before the table is made)
ip_pr('mv.nargin', 'methodsview()');
ip_pr('mv.number', 'methodsview(1)');
ip_pr('mv.charmatrix', 'methodsview([''ab''; ''cd''])');
ip_pr('mv.cell', 'methodsview({''System.Math''})');
ip_pr('mv.badopt', 'methodsview(''System.Math'', ''bogus'')');
ip_px('mv.out.ui', 'x = methodsview(''System.Math'');');
ip_px('mv.out.noui1', 'x = methodsview(''System.Math'', ''noUI'');');
ip_px('mv.unknown', 'methodsview(''No.Such.Klass'')');
ip_px('mv.unknown.string', 'methodsview("No.Such.Klass")');
ip_px('mv.unknown.lib', 'methodsview(''lib.nolib'')');
ip_px('mv.unknown.noui', '[h, d] = methodsview(''No.Such.Klass'', ''noUI''); disp(class(h)); disp(size(h)); disp(class(d)); disp(size(d))');
ip_pr('lfv.nargin', 'libfunctionsview()');
ip_pr('lfv.number', 'libfunctionsview(1)');
ip_pr('lfv.strings', 'libfunctionsview(["a"; "b"])');
ip_pr('lfv.charmatrix', 'libfunctionsview([''ab''; ''cd''])');
ip_px('lfv.notloaded', 'libfunctionsview(''nolib'')');
ip_px('lfv.notloaded.string', 'libfunctionsview("nolib")');
ip_px('lfv.nargout', 'x = libfunctionsview(''nolib'');');

% ---- the rows the window would show
views = {'System.Math', 'JGTest.Vector2', 'JGTest.Modifiers', 'JGTest.Members', 'JGTest.Color', ...
    'System.Text.StringBuilder', 'lib.jgtestlib', 'lib.pointer', 'lib.jg_point'};
for k = 1:numel(views)
    name = views{k};
    try
        [h, d] = methodsview(name, 'noUI');
        fprintf('rows.%s.head\t%s\n', name, strjoin(cellstr(h(:))', ' | '));
        fprintf('rows.%s.size\t%d %d\n', name, size(d, 1), size(d, 2));
        for r = 1:size(d, 1)
            fprintf('rows.%s.%03d\t%s\n', name, r, strjoin(cellstr(d(r, :)), ' || '));
        end
    catch e
        fprintf('rows.%s\tERR %s %s\n', name, e.identifier, e.message);
    end
end

% an object rather than a name
ip_px('rows.obj.sb', '[h, d] = methodsview(System.Text.StringBuilder(''x''), ''noUI''); disp(size(d)); disp(h'')');
ip_px('rows.obj.lp', '[h, d] = methodsview(libpointer(''doublePtr'', 1), ''noUI''); disp(size(d)); disp(h'')');
ip_px('rows.obj.ls', '[h, d] = methodsview(libstruct(''jg_point''), ''noUI''); disp(size(d)); disp(h'')');

% ---- the two-output methods -full it is built on
ip_px('methods2.math', '[m, d] = methods(''System.Math'', ''-full''); disp(class(m)); disp(size(m)); disp(class(d)); disp(size(d))');
ip_px('methods2.vector2.row1', '[m, d] = methods(''JGTest.Vector2'', ''-full''); disp(m{1}); disp(d(1, :))');
ip_px('methods2.lib', '[m, d] = methods(''lib.jgtestlib'', ''-full''); disp(size(m)); disp(size(d)); disp(m{1})');
ip_px('methods2.lib.d1', '[m, d] = methods(''lib.jgtestlib'', ''-full''); disp(d(1, :))');

unloadlibrary(lib);
