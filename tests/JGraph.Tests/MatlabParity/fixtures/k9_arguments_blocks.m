% record: -noFigureWindows
% Compiler and SDK plan, stage K9: the arguments blocks the strongly typed .NET interface is generated
% from - name-value arguments (opts.Name lines), arguments (Input), (Output) and (Repeating) blocks,
% and what nargin and namedargs2cell answer for them. Each function is written to a scratch folder
% and called there.
beep('off');
root = tempname;
mkdir(root);
old = cd(root);

put(root, 'nv1.m', {'function r = nv1(x, opts)', 'arguments', '    x (1,1) double', ...
    '    opts.Scale (1,1) double = 1', '    opts.Label (1,1) string = "none"', 'end', ...
    'r = sprintf(''%g|%g|%s|%s|%d'', x, opts.Scale, opts.Label, strjoin(fieldnames(opts)'', '',''), nargin);', 'end'});
put(root, 'nv2.m', {'function r = nv2(opts)', 'arguments', '    opts.A', '    opts.B (1,1) double = 2', 'end', ...
    'r = strjoin(fieldnames(opts)'', '','');', 'if isfield(opts, ''A''), r = [r ''|'' class(opts.A)]; end', 'end'});
put(root, 'nv3.m', {'function r = nv3(a, b, opts)', 'arguments', '    a', '    b = 5', '    opts.Scale = 1', 'end', ...
    'r = sprintf(''%s|%s|%s'', k9s(a), k9s(b), k9s(opts.Scale));', 'end'});
put(root, 'nv4.m', {'function r = nv4(x, opts)', 'arguments', '    x (1,1) double', ...
    '    opts.Scale (1,1) double = x * 2', '    opts.Mode (1,1) string {mustBeMember(opts.Mode, ["fast" "slow"])} = "fast"', 'end', ...
    'r = sprintf(''%g|%s'', opts.Scale, opts.Mode);', 'end'});
put(root, 'nv5.m', {'function r = nv5(o1, o2)', 'arguments', '    o1.Alpha = 1', '    o2.Beta = 2', 'end', ...
    'r = sprintf(''%s|%s|%s|%s'', strjoin(fieldnames(o1)'', '',''), strjoin(fieldnames(o2)'', '',''), k9s(o1.Alpha), k9s(o2.Beta));', 'end'});
put(root, 'nv6.m', {'function r = nv6(opts)', 'arguments', '    opts.Width (1,1) double = 1', '    opts.WidthMax (1,1) double = 2', 'end', ...
    'r = sprintf(''%g|%g'', opts.Width, opts.WidthMax);', 'end'});
put(root, 'rep1.m', {'function r = rep1(x, y)', 'arguments (Repeating)', '    x (1,1) double', '    y (1,1) string', 'end', ...
    'r = sprintf(''%s|%s|%s|%d'', class(x), mat2str(size(x)), strjoin(cellfun(@k9s, [x y], ''UniformOutput'', false), '',''), nargin);', 'end'});
put(root, 'rep2.m', {'function r = rep2(varargin)', 'arguments (Repeating)', '    varargin (1,1) double {mustBePositive}', 'end', ...
    'r = sprintf(''%d|%s'', numel(varargin), strjoin(cellfun(@k9s, varargin, ''UniformOutput'', false), '',''));', 'end'});
put(root, 'rep3.m', {'function r = rep3(first, rest, opts)', 'arguments', '    first (1,1) double', 'end', ...
    'arguments (Repeating)', '    rest (1,:) char', 'end', 'arguments', '    opts.Sep (1,1) string = "/"', 'end', ...
    'r = sprintf(''%g|%s'', first, strjoin(string(rest), opts.Sep));', 'end'});
put(root, 'out1.m', {'function [a, b] = out1(x)', 'arguments (Input)', '    x (1,:) double', 'end', ...
    'arguments (Output)', '    a (1,1) double', '    b (1,:) char', 'end', 'a = sum(x);', 'b = ''ok'';', 'end'});
put(root, 'out2.m', {'function [a, b] = out2(kind)', 'arguments (Output)', '    a (1,1) int32', '    b (1,1) double {mustBePositive}', 'end', ...
    'switch kind', '    case 1, a = 2.6; b = 1;', '    case 2, a = [1 2]; b = 1;', '    case 3, a = 1; b = -1;', ...
    '    case 4, a = ''x''; b = 1;', '    case 5, a = 1;', 'end', 'end'});
put(root, 'out3.m', {'function v = out3(n)', 'arguments (Input)', '    n (1,1) double', 'end', 'arguments (Output)', ...
    '    v (1,:) double', 'end', 'v = (1:n)'';', 'end'});
put(root, 'nv7.m', {'function r = nv7(opts)', 'arguments', '    opts.Alpha = 1', '    opts.Beta = 2', '    opts.Gamma = 3', 'end', ...
    'r = opts.Alpha + opts.Beta + opts.Gamma;', 'end'});
put(root, 'nv8.m', {'function r = nv8(opts)', 'arguments', '    opts.Only (1,1) double = 1', 'end', 'r = opts.Only;', 'end'});
put(root, 'nv9.m', {'function r = nv9(opts)', 'arguments', '    opts.Al = 1', '    opts.Alp = 2', '    opts.Alpha = 3', 'end', ...
    'r = sprintf(''%g %g %g'', opts.Al, opts.Alp, opts.Alpha);', 'end'});
put(root, 'k9s.m', {'function s = k9s(v)', 'if ischar(v), s = [''c:'' v];', 'elseif isstring(v), s = [''s:'' char(strjoin(v, ''+''))];', ...
    'else, s = [class(v) '':'' mat2str(v)]; end', 'end'});

% Name-value arguments.
u2_chk('nv_none', @() nv1(2));
u2_chk('nv_pair', @() nv1(2, 'Scale', 3));
u2_chk('nv_equals', @() nv1(2, Scale=3));
u2_chk('nv_string_names', @() nv1(2, "Label", "x", "Scale", 4));
u2_chk('nv_case', @() nv1(2, 'scale', 3));
u2_chk('nv_partial', @() nv1(2, 'Sc', 3));
u2_chk('nv_unknown', @() nv1(2, 'Bad', 1));
u2_chk('nv_odd', @() nv1(2, 'Scale'));
u2_chk('nv_class', @() nv1(2, 'Scale', 'x'));
u2_chk('nv_size', @() nv1(2, 'Scale', [1 2]));
u2_chk('nv_twice', @() nv1(2, 'Scale', 1, 'Scale', 5));
u2_chk('nv_number_name', @() nv1(2, 3));
u2_chk('nv_missing_positional', @() nv1());
u2_chk('nv_struct', @() nv1(2, struct('Scale', 3)));
u2_chk('nv_label_convert', @() nv1(2, 'Label', 5));
u2_chk('nv_no_defaults', @() nv2());
u2_chk('nv_no_defaults_given', @() nv2('A', 'x'));
u2_chk('nv_optional_positional', @() nv3(1));
u2_chk('nv_optional_positional_name', @() nv3(1, 'Scale', 2));
u2_chk('nv_optional_positional_value', @() nv3(1, 7, 'Scale', 2));
u2_chk('nv_optional_positional_word', @() nv3(1, 'word'));
u2_chk('nv_default_from_positional', @() nv4(3));
u2_chk('nv_member', @() nv4(3, 'Mode', "slow"));
u2_chk('nv_member_bad', @() nv4(3, 'Mode', "warp"));
u2_chk('nv_two_structs', @() nv5('Beta', 7, 'Alpha', 6));
u2_chk('nv_two_structs_none', @() nv5());
u2_chk('nv_prefix_exact', @() nv6('Width', 5));
u2_chk('nv_prefix_ambiguous', @() nv6('Wid', 5));
u2_chk('nv_prefix_longer', @() nv6('WidthM', 5));
u2_chk('nv_nargin', @() nargin('nv1'));
u2_chk('nv_nargin_nodefaults', @() nargin('nv2'));
u2_chk('nv_nargin_optional', @() nargin('nv3'));
u2_chk('nv_namedargs2cell', @() namedargs2cell(struct('A', 1, 'B', 'two')));
u2_chk('nv_namedargs2cell_empty', @() namedargs2cell(struct()));
u2_chk('nv_namedargs2cell_bad', @() namedargs2cell(5));
u2_chk('nv_forwarded', @() forward(@nv1, 2, struct('Scale', 9, 'Label', "f")));
u2_chk('nv_equals_two', @() nv1(2, Scale=3, Label="x"));
u2_chk('nv_odd_late', @() nv1(2, 'Scale', 3, 'Label'));
u2_chk('nv_cell_value', @() nv1(2, 'Scale', {1}));
u2_chk('nv_three_unknown', @() nv7('Delta', 1));
u2_chk('nv_three_ambiguous', @() nv9('A', 1));
u2_chk('nv_three_prefix', @() nv9('Alph', 1));
u2_chk('nv_three_exact_short', @() nv9('Al', 5));
u2_chk('nv_one_unknown', @() nv8('Other', 1));
u2_chk('nv_one_prefix', @() nv8('o', 4));
u2_chk('nv_name_empty', @() nv8('', 4));
u2_chk('nv_name_string_array', @() nv8(["Only" "x"], 4));

% Repeating arguments.
u2_chk('rep_none', @() rep1());
u2_chk('rep_two_sets', @() rep1(1, "a", 2, "b"));
u2_chk('rep_convert', @() rep1(1, 'a'));
u2_chk('rep_incomplete', @() rep1(1, "a", 2));
u2_chk('rep_bad_value', @() rep1([1 2], "a"));
u2_chk('rep_varargin', @() rep2(1, 2, 3));
u2_chk('rep_varargin_none', @() rep2());
u2_chk('rep_varargin_bad', @() rep2(1, -2));
u2_chk('rep_mixed', @() rep3(1, 'a', 'b', 'Sep', "-"));
u2_chk('rep_mixed_plain', @() rep3(1, 'a', 'b'));
u2_chk('rep_nargin', @() nargin('rep1'));
u2_chk('rep_nargin_mixed', @() nargin('rep3'));

% Input and Output blocks.
u2_chk('out_ok', @() pair(@out1, [1 2 3]));
u2_chk('out_one', @() out1([1 2 3]));
u2_chk('out_convert', @() pair(@out2, 1));
u2_chk('out_convert_class', @() class(out2(1)));
u2_chk('out_size', @() pair(@out2, 2));
u2_chk('out_validator', @() pair(@out2, 3));
u2_chk('out_class', @() pair(@out2, 4));
u2_chk('out_unset', @() pair(@out2, 5));
u2_chk('out_unset_one', @() out2(5));
u2_chk('out_unrequested_invalid', @() out2(3));
u2_chk('out_column_to_row', @() out3(3));
u2_chk('out_input_bad', @() out1('abc'));
u2_chk('out_input_matrix', @() out1([1 2; 3 4]));
u2_chk('out_nargin', @() nargin('out1'));
u2_chk('out_nargout', @() nargout('out1'));

cd(old);
rmdir(root, 's');

function s = pair(f, x)
[a, b] = f(x);
s = sprintf('%s %s | %s %s', class(a), mat2str(a), class(b), mat2str(b));
end

function r = forward(f, x, opts)
args = namedargs2cell(opts);
r = f(x, args{:});
end

function put(folder, name, lines)
fid = fopen(fullfile(folder, name), 'w');
fprintf(fid, '%s\n', lines{:});
fclose(fid);
end
