% probe_views3: methodsview's and libfunctionsview's refusals asked without an output (probe_views
% asked through ip_pr, whose output request met TooManyOutputs first), what methods answers for a
% library that is not loaded, and methodsview under an import. No window opens: every call below
% fails before the table is made, or asks for 'noUI'.
dotnetenv("core", Version="8");
a = ip_assets();
addpath(a.here); addpath(a.root);
loadlibrary(a.lib, @jgtestlib_proto);

ip_px('mv.nargin', 'methodsview()');
ip_px('mv.number', 'methodsview(1)');
ip_px('mv.charmatrix', 'methodsview([''ab''; ''cd''])');
ip_px('mv.cell', 'methodsview({''System.Math''})');
ip_px('mv.struct', 'methodsview(struct(''a'', 1))');
ip_px('mv.strings', 'methodsview(["a" "b"])');
ip_px('mv.badopt.number', 'methodsview(''System.Math'', 5)');
ip_px('mv.badopt.noUI.case', '[h, d] = methodsview(''lib.pointer'', ''NOUI''); disp(size(d))');
ip_px('mv.three', 'methodsview(''System.Math'', ''noUI'', 1)');
ip_px('lfv.nargin', 'libfunctionsview()');
ip_px('lfv.number', 'libfunctionsview(1)');
ip_px('lfv.strings', 'libfunctionsview(["a"; "b"])');
ip_px('lfv.charmatrix', 'libfunctionsview([''ab''; ''cd''])');
ip_px('lfv.cell', 'libfunctionsview({''jgtestlib''})');
ip_px('lfv.two', 'libfunctionsview(''jgtestlib'', 1)');
ip_pr('methods.unloaded.cell', 'methods(''lib.nolib'')');
ip_pr('methods.unloaded.full.cell', 'methods(''lib.nolib'', ''-full'')');
ip_pr('methods.unknown.cell', 'methods(''No.Such.Klass'')');
ip_px('methods.unknown.print', 'methods(''No.Such.Klass'')');
ip_px('mv.import', 'import System.Text.*; [h, d] = methodsview(''StringBuilder'', ''noUI''); disp(size(d))');
ip_px('mv.noui.struct.rows', '[h, d] = methodsview(''lib.jg_point'', ''noUI''); disp(d(14, :)); disp(d(15, :))');
unloadlibrary('jgtestlib');
