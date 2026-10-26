% record: -noFigureWindows
% Compiler and SDK plan, stage K12: C++ shared libraries for the MATLAB Data API -
% compiler.build.CppSharedLibraryOptions, its defaults, derived names and refusals; mcc -W cpplib's
% generic interface, how it names a library and what it refuses; the strongly typed header it
% generates, read back line by line; and subsref, subsasgn and substruct, which that header calls to
% reach a class's properties. Nothing is compiled, so neither engine needs a C++ compiler.
beep('off');
root = tempname;
src = fullfile(root, 'src');
mkdir(src);
put(src, 'k12f.m', {'function [a, b] = k12f(x, n)', '%K12F A probe function.', 'arguments', '    x (1,:) double {mustBeReal}', ...
    '    n (1,1) int32', 'end', 'a = x * double(n); b = numel(x);', 'end'});
put(src, 'k12g.m', {'function k12g(s, c)', '%K12G Nothing out.', 'arguments', '    s (1,1) string', '    c (1,1) k12shade', 'end', 'end'});
put(src, 'k12v.m', {'function r = k12v(varargin)', 'r = nargin;', 'end'});
put(src, 'k12pt.m', {'classdef k12pt', '    properties', '        X (1,1) double = 1', '        Tag (1,:) char = ''p''', '    end', ...
    '    properties (SetAccess = private)', '        Hidden2 (1,1) double = 2', '    end', '    methods', '        function obj = k12pt(x)', ...
    '            if nargin > 0, obj.X = x; end', '        end', '        function d = twice(obj)', '            d = 2 * obj.X;', '        end', ...
    '    end', 'end'});
put(src, 'k12ctr.m', {'classdef k12ctr < handle', '    properties', '        Count (1,1) double {mustBeReal} = 0', '    end', 'end'});
put(src, 'k12shade.m', {'classdef k12shade', '    enumeration', '        light, dark', '    end', 'end'});
put(src, 'k12notes.txt', {'notes'});
old = cd(src);

% The options from one file: the defaults and the names derived from it.
o = compiler.build.CppSharedLibraryOptions('k12f.m');
u2_chk('class', @() class(o));
u2_chk('props', @() strjoin(properties(o)', ' '));
u2_chk('interface', @() o.Interface);
u2_chk('version', @() o.LibraryVersion);
u2_chk('name', @() o.LibraryName);
u2_chk('getname', @() o.getName);
u2_chk('outdir', @() o.OutputDir);
u2_chk('debug', @() char(o.DebugBuild));
u2_chk('samples', @() class(o.SampleGenerationFiles));
u2_chk('samples_size', @() size(o.SampleGenerationFiles));
u2_chk('files', @() rel(o.FunctionFiles, src));
u2_chk('two_name', @() compiler.build.CppSharedLibraryOptions({'k12g.m', 'k12f.m'}).LibraryName);
u2_chk('given_name', @() compiler.build.CppSharedLibraryOptions('k12f.m', 'LibraryName', 'K12Named').OutputDir);
u2_chk('nv_interface', @() compiler.build.CppSharedLibraryOptions('k12f.m', 'Interface', 'mwarray').Interface);

% The constructor's refusals.
u2_chk('none', @() err(@() compiler.build.CppSharedLibraryOptions()));
u2_chk('missing', @() err(@() compiler.build.CppSharedLibraryOptions('k12nosuch.m')));
u2_chk('not_code', @() err(@() compiler.build.CppSharedLibraryOptions('k12notes.txt')));
u2_chk('number', @() err(@() compiler.build.CppSharedLibraryOptions(5)));
u2_chk('ambiguous_nv', @() err(@() compiler.build.CppSharedLibraryOptions('k12f.m', 'Library', 'P')));
u2_chk('embed_nv', @() err(@() compiler.build.CppSharedLibraryOptions('k12f.m', 'EmbedArchive', 'on')));
u2_chk('folder_mwarray', @() err(@() compiler.build.CppSharedLibraryOptions({src}, 'Interface', 'mwarray')));

% Each property's checks.
o = compiler.build.CppSharedLibraryOptions('k12f.m');
u2_chk('set_interface_upper', @() setp(o, 'Interface', 'MWARRAY'));
u2_chk('set_interface_mixed', @() setp(o, 'Interface', 'Matlab-Data'));
u2_chk('set_interface_bad', @() err(@() setp(o, 'Interface', 'bogus')));
u2_chk('set_interface_number', @() err(@() setp(o, 'Interface', 3)));
u2_chk('set_interface_empty', @() err(@() setp(o, 'Interface', '')));
u2_chk('set_version_warns', @() warned(@() setp(o, 'LibraryVersion', '1.2')));
u2_chk('set_version_empty', @() setp(o, 'LibraryVersion', ''));
u2_chk('set_name_digit', @() setp(o, 'LibraryName', '1abc'));
u2_chk('set_name_dotted', @() setp(o, 'LibraryName', 'Org.Group'));
u2_chk('set_name_slash', @() err(@() setp(o, 'LibraryName', 'a/b')));
u2_chk('set_samples_missing', @() err(@() setp(o, 'SampleGenerationFiles', 'k12nosuch.m')));
u2_chk('set_debug_on', @() char(setp(o, 'DebugBuild', 'on')));

% mcc -W cpplib's generic interface, and its refusals.
u2_chk('mcc_generic', @() build(root, 'generic', {'-W', 'cpplib:k12gen,generic', '-T', 'link:lib', 'k12f.m', 'k12g.m', 'k12v.m', 'k12pt.m', 'k12ctr.m', 'k12shade.m'}));
u2_chk('mcc_version', @() build(root, 'version', {'-W', 'cpplib:k12ver,generic,version=2.0', '-T', 'link:lib', 'k12f.m'}));
u2_chk('mcc_codegen', @() build(root, 'codegen', {'-W', 'cpplib:k12cg,generic', '-T', 'codegen', 'k12f.m'}));
u2_chk('mcc_exe', @() build(root, 'exe', {'-W', 'cpplib:k12ex,generic', '-T', 'link:exe', 'k12f.m'}));
u2_chk('mcc_twice', @() build(root, 'twice', {'-W', 'cpplib:k12t1,generic', '-W', 'cpplib:k12t2,generic', 'k12f.m'}));
u2_chk('mcc_o', @() build(root, 'o', {'-W', 'cpplib:k12on,generic', '-o', 'k12other', 'k12f.m'}));
u2_chk('mcc_badname', @() build(root, 'badname', {'-W', 'cpplib:1bad,generic', 'k12f.m'}));
u2_chk('mcc_dash', @() build(root, 'dash', {'-W', 'cpplib:a-b,generic', 'k12f.m'}));
u2_chk('mcc_noname', @() build(root, 'noname', {'-W', 'cpplib:', 'k12f.m'}));
u2_chk('mcc_named', @() build(root, 'named', {'-W', 'cpplib:k12nm,named', 'k12f.m'}));
u2_chk('mcc_badarg', @() build(root, 'badarg', {'-W', 'cpplib:k12ba,bogus', 'k12f.m'}));
u2_chk('mcc_dash_c', @() build(root, 'dash_c', {'-W', 'cpplib:k12cc,generic', '-c', 'k12f.m'}));
gen = fullfile(root, 'generic', 'v2', 'generic_interface', 'k12genv2.hpp');
u2_chk('gen_includes', @() grab(gen, '^#include'));
u2_chk('gen_classes', @() grab(gen, '^ *(class|enum class) '));
u2_chk('gen_constructors', @() grab(gen, '^ +k12(pt|ctr)\('));
u2_chk('gen_properties', @() grab(gen, ' (get|set)[A-Z]\w*\('));
u2_chk('gen_friends', @() grab(gen, 'friend bool operator'));
u2_chk('gen_templates', @() grab(gen, '^ *typename return_type_'));
u2_chk('gen_specializations', @() grab(gen, '^ *struct (\w+::)?return_type_\w+<'));
u2_chk('gen_calls', @() grab(gen, 'feval\(u"'));
u2_chk('gen_enum', @() grab(gen, 'static const std::map'));
u2_chk('gen_arrays', @() grab(gen, '_arrayFactory\.create'));

% compiler.build.cppSharedLibrary's own refusals, before any build.
u2_chk('api_wrong_options', @() err(@() compiler.build.cppSharedLibrary(compiler.build.StandaloneApplicationOptions('k12f.m'))));
u2_chk('api_no_args', @() err(@() compiler.build.cppSharedLibrary()));
u2_chk('api_two_out', @() err(@() twoOut()));
u2_chk('api_extra', @() err(@() compiler.build.cppSharedLibrary(o, 'Verbose', 'on')));

% substruct, subsref and subsasgn.
s1 = substruct('.', 'a');
u2_chk('substruct_class', @() class(s1));
u2_chk('substruct_size', @() size(s1));
u2_chk('substruct_fields', @() strjoin(fieldnames(s1)', ' '));
u2_chk('substruct_type', @() s1.type);
u2_chk('substruct_subs', @() s1.subs);
s2 = substruct('()', {1, ':'}, '.', 'b', '{}', {2});
u2_chk('substruct_chain_size', @() size(s2));
u2_chk('substruct_chain_types', @() strjoin({s2.type}, ' '));
u2_chk('substruct_chain_subs', @() class(s2(1).subs));
u2_chk('substruct_bad_type', @() err(@() substruct('x', 1)));
u2_chk('substruct_paren_not_cell', @() err(@() substruct('()', 1)));
u2_chk('substruct_dot_not_text', @() err(@() substruct('.', 1)));
u2_chk('substruct_odd', @() err(@() substruct('()')));
u2_chk('substruct_none', @() err(@() substruct()));
st = struct('a', [10 20 30], 'c', {{'x', 'y'}});
u2_chk('subsref_field', @() subsref(st, substruct('.', 'a')));
u2_chk('subsref_field_index', @() subsref(st, substruct('.', 'a', '()', {2})));
u2_chk('subsref_matrix_row', @() subsref(magic(3), substruct('()', {2, ':'})));
u2_chk('subsref_colon_char', @() subsref(1:4, substruct('()', {':'})));
u2_chk('subsref_cell_brace', @() subsref({1, 'two'}, substruct('{}', {2})));
u2_chk('subsref_cell_paren', @() class(subsref({1, 'two'}, substruct('()', {2}))));
u2_chk('subsref_nested', @() subsref(st, substruct('.', 'c', '{}', {1})));
u2_chk('subsref_struct_built', @() subsref(st, struct('type', '.', 'subs', 'a')));
u2_chk('subsref_object', @() subsref(k12pt(4), substruct('.', 'X')));
u2_chk('subsref_method', @() subsref(k12pt(4), substruct('.', 'twice')));
u2_chk('subsref_handle', @() subsref(k12ctr, substruct('.', 'Count')));
u2_chk('subsref_no_field', @() err(@() subsref(st, substruct('.', 'nosuch'))));
u2_chk('subsref_past_end', @() err(@() subsref(1:3, substruct('()', {5}))));
u2_chk('subsref_bad_type', @() err(@() subsref(st, struct('type', 'x', 'subs', 'a'))));
u2_chk('subsref_not_struct', @() err(@() subsref(1, 2)));
u2_chk('subsref_one_arg', @() err(@() subsref(1)));
u2_chk('subsasgn_field', @() subsref(subsasgn(st, substruct('.', 'b'), 5), substruct('.', 'b')));
u2_chk('subsasgn_matrix', @() subsasgn(zeros(2), substruct('()', {1, 2}), 9));
u2_chk('subsasgn_grow', @() subsasgn(1:2, substruct('()', {4}), 7));
u2_chk('subsasgn_cell', @() strjoin(cellfun(@class, subsasgn({}, substruct('{}', {3}), 'z'), 'UniformOutput', false), ' '));
u2_chk('subsasgn_nested', @() subsref(subsasgn(st, substruct('.', 'a', '()', {1}), 99), substruct('.', 'a')));
u2_chk('subsasgn_object', @() subsref(subsasgn(k12pt(4), substruct('.', 'X'), 7), substruct('.', 'X')));
u2_chk('subsasgn_object_class', @() class(subsasgn(k12pt(4), substruct('.', 'X'), 7)));
xm = [1 2 3];
ym = subsasgn(xm, substruct('()', {1}), 9);
u2_chk('subsasgn_leaves_matrix', @() [xm ym]);
sm.a = [4 5];
tm = subsasgn(sm, substruct('.', 'a', '()', {2}), 0);
u2_chk('subsasgn_leaves_struct', @() [sm.a tm.a]);
cm = {1, 2};
dm = subsasgn(cm, substruct('{}', {1}), 'z');
u2_chk('subsasgn_leaves_cell', @() [class(cm{1}) ' ' class(dm{1})]);
rm = subsref(magic(3), substruct('()', {2, ':'}));
rm(1) = 100;
u2_chk('subsref_gives_a_copy', @() rm);
h = k12ctr;
h2 = subsasgn(h, substruct('.', 'Count'), 3); %#ok<NASGU>
u2_chk('subsasgn_handle_in_place', @() h.Count);
u2_chk('subsasgn_no_output', @() err(@() noOutput(h)));
u2_chk('subsasgn_private', @() errOut(@() subsasgn(k12pt, substruct('.', 'Hidden2'), 1)));
u2_chk('subsasgn_validation', @() errOut(@() subsasgn(k12ctr, substruct('.', 'Count'), 1i)));
u2_chk('subsasgn_two_args', @() errOut(@() subsasgn(1, substruct('()', {1}))));
u2_chk('subsasgn_bad_type', @() errOut(@() subsasgn(1, struct('type', 'x', 'subs', 'a'), 2)));

% The checks a subscript structure passes before any level is indexed, in R2025b's order, and the
% comma-separated lists subsref gives.
x3 = magic(3);
c3 = {1, 'two', [3 3 3]};
s3 = struct('a', {10, 20});
u2_chk('subsref_no_type', @() err(@() subsref(x3, struct('subs', {{1}}))));
u2_chk('subsref_no_fields', @() err(@() subsref(x3, struct([]))));
u2_chk('subsref_extra_field', @() err(@() subsref(x3, struct('type', '()', 'subs', {{2}}, 'extra', 1))));
u2_chk('subsref_wrong_fields', @() err(@() subsref(x3, struct('type', '()', 'other', 1))));
u2_chk('subsref_wrong_fields_empty', @() err(@() subsref(x3, struct('a', {}, 'b', {}))));
u2_chk('subsref_fields_reversed', @() subsref(x3, struct('subs', {{2}}, 'type', '()')));
u2_chk('subsref_empty', @() err(@() subsref(x3, struct('type', {}, 'subs', {}))));
u2_chk('subsref_type_number', @() err(@() subsref(x3, struct('type', 1, 'subs', {{1}}))));
u2_chk('subsref_type_cell', @() err(@() subsref(x3, struct('type', {{'()'}}, 'subs', {{1}}))));
u2_chk('subsref_type_column', @() err(@() subsref(x3, struct('type', ['('; ')'], 'subs', {{2}}))));
u2_chk('subsref_type_strings', @() err(@() subsref(x3, struct('type', {["()", "{}"]}, 'subs', {{2}}))));
u2_chk('subsref_type_string', @() subsref(x3, struct('type', "()", 'subs', {{2}})));
u2_chk('subsref_type_empty', @() err(@() subsref(x3, struct('type', '', 'subs', {{2}}))));
u2_chk('subsref_type_space', @() err(@() subsref(x3, struct('type', ' ()', 'subs', {{2}}))));
u2_chk('subsref_paren_not_cell', @() err(@() subsref(x3, struct('type', '()', 'subs', 1))));
u2_chk('subsref_paren_char', @() err(@() subsref(x3, struct('type', '()', 'subs', 'a'))));
u2_chk('subsref_brace_not_cell', @() err(@() subsref(c3, struct('type', '{}', 'subs', 1))));
u2_chk('subsref_dot_number', @() err(@() subsref(st, struct('type', '.', 'subs', 1))));
u2_chk('subsref_dot_cell', @() err(@() subsref(st, struct('type', '.', 'subs', {{'a'}}))));
u2_chk('subsref_dot_string', @() subsref(st, struct('type', '.', 'subs', "a")));
u2_chk('subsref_dot_empty', @() err(@() subsref(st, struct('type', '.', 'subs', ''))));
u2_chk('subsref_later_type_first', @() err(@() subsref(x3, struct('type', {'()', '[]'}, 'subs', {{100}, {1}}))));
u2_chk('subsref_later_number_type', @() err(@() subsref(x3, struct('type', {'()', 1}, 'subs', {{100}, {1}}))));
u2_chk('subsref_paren_then_paren', @() err(@() subsref(x3, struct('type', {'()', '()'}, 'subs', {{100}, 5}))));
u2_chk('subsref_paren_then_brace', @() err(@() subsref(c3, struct('type', {'()', '{}'}, 'subs', {{1}, {1}}))));
u2_chk('subsref_level_order', @() err(@() subsref(x3, struct('type', {'()', 7}, 'subs', {5, {1}}))));
u2_chk('subsref_column_levels', @() subsref(st, struct('type', {'.'; '()'}, 'subs', {'a'; {2}})));
u2_chk('subsref_brace_after_brace', @() subsref({c3}, substruct('{}', {1}, '{}', {2})));
u2_chk('subsref_paren_after_brace', @() subsref(c3, substruct('{}', {3}, '()', {2})));
u2_chk('subsref_dot_after_paren', @() subsref(s3, substruct('()', {2}, '.', 'a')));
u2_chk('subsref_brace_no_index', @() subsref(c3, struct('type', '{}', 'subs', {{}})));
u2_chk('subsref_handle_call', @() subsref(@(v) v + 1, substruct('()', {2})));
u2_chk('subsref_logical_index', @() subsref(x3, substruct('()', {logical([1 0 1])})));
u2_chk('subsref_list_first', @() subsref(c3, substruct('{}', {':'})));
u2_chk('subsref_list_fields_first', @() subsref(s3, substruct('.', 'a')));
u2_chk('subsref_list_three', @() listOf(3, c3, substruct('{}', {':'})));
u2_chk('subsref_list_two_fields', @() listOf(2, s3, substruct('.', 'a')));
u2_chk('subsref_list_range_fields', @() listOf(2, s3, substruct('()', {1:2}, '.', 'a')));
u2_chk('subsref_list_braces', @() listOf(2, c3, substruct('{}', {1:2})));
u2_chk('subsref_list_short', @() err(@() listOf(2, x3, substruct('()', {1:2}))));
u2_chk('subsref_list_four', @() err(@() listOf(4, c3, substruct('{}', {':'}))));
u2_chk('subsref_list_none', @() errOut(@() subsref(c3, substruct('{}', {[]}))));
u2_chk('subsasgn_paren_no_index', @() errOut(@() subsasgn(x3, struct('type', '()', 'subs', {{}}), 5)));
u2_chk('subsasgn_no_index_first', @() errOut(@() subsasgn(st, substruct('()', {}, '.', 'a'), 1)));
u2_chk('subsasgn_no_index_last', @() errOut(@() subsasgn(st, substruct('.', 'a', '()', {}), 1)));
u2_chk('subsasgn_paren_then_paren', @() errOut(@() subsasgn(x3, struct('type', {'()', '()'}, 'subs', {{1}, {1}}), 0)));
u2_chk('subsasgn_later_type_first', @() errOut(@() subsasgn(x3, struct('type', {'()', '[]'}, 'subs', {{100}, {1}}), 1)));
u2_chk('subsasgn_empty', @() errOut(@() subsasgn(x3, struct('type', {}, 'subs', {}), 0)));
u2_chk('subsasgn_no_type', @() errOut(@() subsasgn(x3, struct('subs', {{1}}), 0)));
u2_chk('subsasgn_dot_number', @() errOut(@() subsasgn(st, struct('type', '.', 'subs', 1), 0)));
u2_chk('subsasgn_paren_not_cell', @() errOut(@() subsasgn(x3, struct('type', '()', 'subs', 1), 0)));
u2_chk('subsasgn_dot_colon', @() subsref(subsasgn(st, substruct('.', 'a', '()', {':'}), 0), substruct('.', 'a')));
u2_chk('subsasgn_new_field', @() strjoin(fieldnames(subsasgn(st, substruct('.', 'b'), 7))', ' '));
u2_chk('subsasgn_delete_row', @() subsasgn(x3, substruct('()', {1, ':'}), []));
u2_chk('subsasgn_brace_grow', @() size(subsasgn(c3, substruct('{}', {5}), 'five')));
u2_chk('subsasgn_mismatch', @() errOut(@() subsasgn(x3, substruct('()', {1:2}), [1 2 3])));
u2_chk('subsasgn_two_outputs', @() err(@() twoFromSubsasgn(x3)));
u2_chk('subs_argument_counts', @()[nargout('subsref') nargout('subsasgn') nargin('subsref') nargin('subsasgn')]);

% properties of MATLAB's own value classes, and the value type a containers.Map takes from its values,
% which the C++ client reads back (k12_client: obj.back.*).
u2_chk('props_datetime', @() strjoin(properties(datetime(2026, 10, 24))', ' '));
u2_chk('props_duration', @() strjoin(properties(seconds(90))', ' '));
u2_chk('props_caldur', @() strjoin(properties(calmonths(2))', ' '));
u2_chk('props_categorical', @() size(properties(categorical({'lo', 'hi'}))));
u2_chk('props_map', @() strjoin(properties(containers.Map({'k'}, {1}))', ' '));
u2_chk('props_table', @() strjoin(properties(table([1; 2], {'a'; 'b'}))', ' '));
u2_chk('props_timetable', @() strjoin(properties(timetable(seconds([1; 2]), [3; 4]))', ' '));
u2_chk('props_string', @() size(properties("s")));
u2_chk('props_cell', @() size(properties({1})));
u2_chk('map_num', @() containers.Map({'a', 'b'}, {1, 2}).ValueType);
u2_chk('map_mixednum', @() containers.Map({'a', 'b'}, {1, int8(2)}).ValueType);
u2_chk('map_int8', @() containers.Map({'a', 'b'}, {int8(1), int8(2)}).ValueType);
u2_chk('map_logical', @() containers.Map({'a'}, {true}).ValueType);
u2_chk('map_char', @() containers.Map({'a', 'b'}, {'x', 'yy'}).ValueType);
u2_chk('map_string', @() containers.Map({'a'}, {"s"}).ValueType);
u2_chk('map_vector', @() containers.Map({'a'}, {[1 2]}).ValueType);
u2_chk('map_mixed', @() containers.Map({'a', 'b'}, {1, 'x'}).ValueType);
u2_chk('map_complex', @() containers.Map({'a'}, {1i}).ValueType);
u2_chk('map_scalar', @() containers.Map('a', 5).ValueType);
u2_chk('map_numkeys', @() containers.Map([1 2], {'a', 'b'}).ValueType);
u2_chk('map_empty', @() containers.Map().ValueType);
cd(old);
rmdir(root, 's');

function v = setp(o, name, value)
% Sets a property on a copy of the options and answers what it then reads.
o.(name) = value;
v = o.(name);
end

function s = warned(f)
% What f answers, and the identifier of the warning it raised.
lastwarn('', '');
[~] = evalc('v = f();');
[~, id] = lastwarn;
s = sprintf('%s | warn %s', u2_text(v), id);
end

function twoOut()
[a, b] = compiler.build.cppSharedLibrary('k12f.m'); %#ok<ASGLU>
end

function noOutput(h)
% subsasgn called as a statement, asked for nothing.
subsasgn(h, substruct('.', 'Count'), 4);
end

function s = listOf(n, v, S)
% The n outputs subsref gives, as u2_text shows each.
switch n
    case 2
        [a, b] = subsref(v, S);
        out = {a, b};
    case 3
        [a, b, c] = subsref(v, S);
        out = {a, b, c};
    otherwise
        [a, b, c, d] = subsref(v, S);
        out = {a, b, c, d};
end
s = strjoin(cellfun(@u2_text, out, 'UniformOutput', false), ' | ');
end

function twoFromSubsasgn(x)
% subsasgn asked for two outputs.
[a, b] = subsasgn(x, substruct('()', {1}), 0); %#ok<ASGLU>
end

function s = build(root, name, args)
% A build's outcome: ok or its refusal, the warning it raised, and the files it wrote.
folder = fullfile(root, name);
lastwarn('', '');
s = err(@() quiet(args, folder));
[~, id] = lastwarn;
s = sprintf('%s | warn %s | files %s', s, id, names(folder));
end

function quiet(args, folder)
% A build with what it prints left out.
evalc('mcc(args{:}, ''-d'', folder)');
end

function s = err(f)
try
    f();
    s = 'no error';
catch e
    s = [e.identifier ' / ' strrep(e.message, newline, ' / ')];
end
end

function s = errOut(f)
% f's refusal when its answer is asked for.
try
    v = f(); %#ok<NASGU>
    s = 'no error';
catch e
    s = [e.identifier ' / ' strrep(e.message, newline, ' / ')];
end
end

function s = names(folder)
% The files a build wrote, those in v2\generic_interface named with the folder.
if ~exist(folder, 'dir')
    s = '';
    return
end
d = dir(fullfile(folder, '**', '*'));
d = d(~[d.isdir]);
files = cellfun(@(f, n) strrep(fullfile(f, n), [folder filesep], ''), {d.folder}, {d.name}, 'UniformOutput', false);
s = strjoin(sort(strrep(files, filesep, '/')), ' ');
end

function s = grab(file, pattern)
% The lines of a generated file that match, joined; the file's header (its first comment) is left out.
text = strrep(fileread(file), sprintf('\r\n'), newline);
text = regexprep(text, '^/\*.*?\*/\n', '', 'once');
lines = splitlines(text);
keep = lines(~cellfun(@isempty, regexp(lines, pattern, 'once')));
s = strjoin(strtrim(keep)', ' / ');
end

function s = rel(files, folder)
files = cellfun(@(f) strrep(f, [folder filesep], ''), cellstr(files), 'UniformOutput', false);
s = strjoin(files(:)', ' ');
end

function put(folder, name, lines)
fid = fopen(fullfile(folder, name), 'w');
fprintf(fid, '%s\n', lines{:});
fclose(fid);
end
