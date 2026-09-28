% views_methods.m -- what methods says of a library, a lib.pointer and a libstruct, and the table
% methodsview and libfunctionsview build on it (interop plan, stage 10). Nothing here opens a
% window: methodsview is asked only for its 'noUI' answer or for a refusal, both of which come
% before its table is shown.
%
% JGraph loads jg_point_make, a function returning a struct by value, which R2025b cannot (ADR 0181),
% so the rows that list or count a library's functions are div=ADR0181.

dotnetenv("core", Version="8");
p = interop_paths();
addpath(p.root);
NET.addAssembly(p.assembly);
lib = 'jgtestlib';
if libisloaded(lib), unloadlibrary(lib); end
loadlibrary(p.lib, @jgtestlib_proto);
lp = libpointer('doublePtr', [1 2 3]);
ls = libstruct('jg_point');

% ---- methods of a pointer and a libstruct
ix_chk('pointer_names', ix_flat(evalc('methods(lp)')));
ix_chk('pointer_names_by_class', ix_flat(evalc('methods(''lib.pointer'')')));
ix_chk('pointer_full', ix_flat(evalc('methods(lp, ''-full'')')));
ix_chk('pointer_cell', strjoin(methods(lp)', ','));
ix_chk('pointer_full_cell', strjoin(methods(lp, '-full')', ' ; '));
ix_chk('struct_names', ix_flat(evalc('methods(ls)')));
ix_chk('struct_names_by_class', ix_flat(evalc('methods(''lib.jg_point'')')));
ix_chk('struct_full', ix_flat(evalc('methods(ls, ''-full'')')));
ix_chk('struct_full_cell', strjoin(methods(ls, '-full')', ' ; '));

% ---- methods of a library
ix_chk('lib_names', ix_flat(evalc('methods(''lib.jgtestlib'')')), 'div=ADR0181');
ix_chk('lib_names_command', ix_flat(evalc('methods lib.jgtestlib')), 'div=ADR0181');
ix_chk('lib_full', ix_flat(evalc('methods(''lib.jgtestlib'', ''-full'')')), 'div=ADR0181');
m = methods('lib.jgtestlib', '-full');
ix_chk('lib_full_count', numel(m), 'div=ADR0181');
ix_chk('lib_full_first', m{1});
ix_chk('lib_full_data', m{13});
ix_chk('lib_full_novoid', m{11});
ix_chk('lib_unloaded_print', ix_flat(evalc('methods(''lib.nolib'')')));
ix_chk('lib_unloaded_cell', ix_show(methods('lib.nolib')));
ix_chk('lib_unloaded_full_cell', ix_show(methods('lib.nolib', '-full')));

% ---- methodsview's table ('noUI')
ix_chk('mv_pointer', mv_rows('lib.pointer'), 'div=ADR0183'); % R2025b blanks an Inherited From by an order it does not keep
ix_chk('mv_pointer_object', mv_rows(lp), 'div=ADR0183');
ix_chk('mv_struct', mv_rows('lib.jg_point'), 'div=ADR0183');
ix_chk('mv_struct_object', mv_rows(ls), 'div=ADR0183');
ix_chk('mv_lib_head', mv_head('lib.jgtestlib'), 'div=ADR0181');
ix_chk('mv_lib_first_rows', mv_first('lib.jgtestlib', 13));
ix_chk('mv_noui_case', mv_head('lib.pointer', 'NOUI'));
ix_chk('mv_unknown_noui', mv_head('No.Such.Klass'));
ix_chk('mv_net_math_head', mv_head('System.Math'));
ix_chk('mv_net_vector2_head', mv_head('JGTest.Vector2'));
ix_chk('mv_net_modifiers_first', mv_first('JGTest.Modifiers', 15));

% ---- methodsview's refusals
ix_chk('mv_nargin', ix_id(@() methodsview()));
ix_chk('mv_nargin_msg', ix_msg(@() methodsview()));
ix_chk('mv_number', ix_id(@() methodsview(1)));
ix_chk('mv_number_msg', ix_msg(@() methodsview(1)));
ix_chk('mv_char_matrix', ix_id(@() methodsview(['ab'; 'cd'])));
ix_chk('mv_cell', ix_id(@() methodsview({'System.Math'})));
ix_chk('mv_struct_value', ix_id(@() methodsview(struct('a', 1))));
ix_chk('mv_string_array', ix_id(@() methodsview(["a" "b"])));
ix_chk('mv_bad_option', ix_id(@() methodsview('System.Math', 'bogus')));
ix_chk('mv_bad_option_msg', ix_msg(@() methodsview('System.Math', 'bogus')));
ix_chk('mv_option_number', ix_id(@() methodsview('System.Math', 5)));
ix_chk('mv_three_args', ix_id(@() methodsview('System.Math', 'noUI', 1)));
ix_chk('mv_one_output', ix_id(@() mv_one('System.Math')));
ix_chk('mv_one_output_msg', ix_msg(@() mv_one('System.Math')));
ix_chk('mv_noui_one_output', ix_id(@() mv_one_noui('System.Math')));
ix_chk('mv_noui_one_output_msg', ix_msg(@() mv_one_noui('System.Math')));
ix_chk('mv_unknown', ix_id(@() methodsview('No.Such.Klass')));
ix_chk('mv_unknown_msg', ix_msg(@() methodsview('No.Such.Klass')));
ix_chk('mv_unknown_lib_msg', ix_msg(@() methodsview('lib.nolib')));

% ---- libfunctionsview's refusals
ix_chk('lfv_nargin', ix_id(@() libfunctionsview()));
ix_chk('lfv_nargin_msg', ix_msg(@() libfunctionsview()));
ix_chk('lfv_number', ix_id(@() libfunctionsview(1)));
ix_chk('lfv_number_msg', ix_msg(@() libfunctionsview(1)));
ix_chk('lfv_strings', ix_id(@() libfunctionsview(["a"; "b"])));
ix_chk('lfv_char_matrix', ix_id(@() libfunctionsview(['ab'; 'cd'])));
ix_chk('lfv_cell', ix_id(@() libfunctionsview({'jgtestlib'})));
ix_chk('lfv_two_args', ix_id(@() libfunctionsview('jgtestlib', 1)));
ix_chk('lfv_output', ix_id(@() lfv_one('nolib')));
ix_chk('lfv_unloaded', ix_id(@() libfunctionsview('nolib')));
ix_chk('lfv_unloaded_msg', ix_msg(@() libfunctionsview('nolib')));
ix_chk('lfv_unloaded_string_msg', ix_msg(@() libfunctionsview("nolib")));

clear lp ls
unloadlibrary(lib);

function s = mv_rows(x)
% The headers, the size and every row of methodsview(x, 'noUI'), one line.
[h, d] = methodsview(x, 'noUI');
rows = strings(size(d, 1), 1);
for r = 1:size(d, 1)
    rows(r) = strjoin(d(r, :), ' ; ');
end
s = char(strjoin(h', ' ; ') + " :: " + mat2str(size(d)) + " :: " + strjoin(rows', ' // '));
end

function s = mv_head(x, option)
% The headers, their size and class, and the size of the rows of methodsview(x, 'noUI').
if nargin < 2, option = 'noUI'; end
[h, d] = methodsview(x, option);
s = [class(h) ' ' mat2str(size(h)) ' ' char(strjoin(h', ' ; ')) ' :: ' class(d) ' ' mat2str(size(d))];
end

function s = mv_first(x, n)
% The first n rows of methodsview(x, 'noUI').
[~, d] = methodsview(x, 'noUI');
rows = strings(n, 1);
for r = 1:n
    rows(r) = strjoin(d(r, :), ' ; ');
end
s = char(strjoin(rows', ' // '));
end

function mv_one(x)
v = methodsview(x); %#ok<NASGU>
end

function mv_one_noui(x)
v = methodsview(x, 'noUI'); %#ok<NASGU>
end

function lfv_one(x)
v = libfunctionsview(x); %#ok<NASGU>
end
