% record: -noFigureWindows
% Compiler and SDK plan, stage K9: enumeration classes - members reached through the class, their
% names as text, comparisons with each other and with text, isenum and enumeration, arrays of
% members, the display, switch, and an enumeration as the declared class of an arguments line. The
% MATLAB Data API passes them to and from .NET by name. The classes are written to a scratch folder
% and the checks run from a script written beside them, which is parsed only once they exist.
beep('off');
root = tempname;
mkdir(root);
old = cd(root);

put(root, 'k9shade.m', {'classdef k9shade', '    enumeration', '        red', '        green', '        blue', '    end', 'end'});
put(root, 'k9size.m', {'classdef k9size', '    properties', '        Width', '    end', '    methods', '        function obj = k9size(w)', ...
    '            obj.Width = w;', '        end', '        function d = doubled(obj)', '            d = 2 * obj.Width;', '        end', '    end', ...
    '    enumeration', '        small (1), medium (5)', '        large (10)', '    end', 'end'});
put(root, 'k9pick.m', {'function r = k9pick(c)', 'arguments', '    c (1,1) k9shade', 'end', 'r = char(c);', 'end'});
put(root, 'k9which.m', {'function r = k9which(c)', 'switch c', '    case k9shade.red', '        r = 1;', '    case {k9shade.green, k9shade.blue}', ...
    '        r = 2;', '    otherwise', '        r = 3;', 'end', 'end'});
put(root, 'k9show.m', {'function t = k9show(code)', 't = evalc(code);', 't = regexprep(t, ''<a [^>]*>|</a>'', '''');', 'end'});
put(root, 'k9divchk.m', {'function k9divchk(name, fn)', '% A recorded divergence (ADR 0265): char of an array of members answers their names here.', ...
    'try', '    v = u2_text(fn());', 'catch err', '    v = err.identifier;', 'end', 'fprintf(''CHK|%s|%s|div=ADR0265\n'', name, v);', 'end'});
put(root, 'k9second.m', {'function r = k9second(f)', '[~, r] = f();', 'end'});
put(root, 'k9setwidth.m', {'function r = k9setwidth(s)', 's.Width = 99;', 'r = s.Width;', 'end'});
put(root, 'k9enumbody.m', {
    'g = k9shade.green;'
    'u2_chk(''class'', @() class(g));'
    'u2_chk(''isenum'', @() isenum(g));'
    'u2_chk(''isenum_number'', @() isenum(5));'
    'u2_chk(''isobject'', @() isobject(g));'
    'u2_chk(''char'', @() char(g));'
    'u2_chk(''string'', @() string(g));'
    'u2_chk(''cellstr'', @() cellstr(g));'
    'u2_chk(''eq_same'', @() g == k9shade.green);'
    'u2_chk(''eq_other'', @() g == k9shade.red);'
    'u2_chk(''ne'', @() g ~= k9shade.red);'
    'u2_chk(''eq_char'', @() g == ''green'');'
    'u2_chk(''eq_string'', @() g == "blue");'
    'u2_chk(''eq_char_unknown'', @() g == ''purple'');'
    'u2_chk(''isequal_same'', @() isequal(g, k9shade.green));'
    'u2_chk(''isequal_other'', @() isequal(g, k9shade.red));'
    'u2_chk(''isequal_char'', @() isequal(g, ''green''));'
    'a = [k9shade.red k9shade.blue k9shade.red];'
    'u2_chk(''array_class'', @() class(a));'
    'u2_chk(''array_size'', @() size(a));'
    'k9divchk(''array_char'', @() char(a));'
    'u2_chk(''array_string'', @() string(a));'
    'u2_chk(''array_cellstr'', @() cellstr(a));'
    'u2_chk(''array_eq'', @() a == k9shade.red);'
    'u2_chk(''array_eq_char'', @() a == ''blue'');'
    'u2_chk(''array_index'', @() char(a(2)));'
    'u2_chk(''ismember'', @() ismember(k9shade.blue, a));'
    'u2_chk(''ismember_array'', @() ismember(a, k9shade.red));'
    'u2_chk(''ismember_char'', @() ismember(g, {''red'', ''green''}));'
    'k9divchk(''enumeration_name'', @() char(enumeration(''k9shade'')));'
    'u2_chk(''enumeration_member'', @() size(enumeration(g)));'
    'u2_chk(''enumeration_names'', @() k9second(@() enumeration(''k9shade'')));'
    'u2_chk(''enumeration_print'', @() evalc(''enumeration(''''k9shade'''')''));'
    'u2_chk(''display'', @() k9show(''k9shade.green''));'
    'u2_chk(''display_named'', @() k9show(''h = k9shade.blue''));'
    'u2_chk(''disp'', @() k9show(''disp(k9shade.green)''));'
    'u2_chk(''display_array'', @() k9show(''[k9shade.red k9shade.blue]''));'
    'u2_chk(''display_column'', @() k9show(''[k9shade.red; k9shade.blue]''));'
    'u2_chk(''disp_array'', @() k9show(''disp([k9shade.red k9shade.blue])''));'
    'u2_chk(''switch_red'', @() k9which(k9shade.red));'
    'u2_chk(''switch_blue'', @() k9which(k9shade.blue));'
    'u2_chk(''switch_char'', @() k9which(''red''));'
    'u2_chk(''argument_member'', @() k9pick(k9shade.blue));'
    'u2_chk(''argument_char'', @() k9pick(''green''));'
    'u2_chk(''argument_string'', @() k9pick("red"));'
    'u2_chk(''argument_unknown'', @() k9pick(''purple''));'
    'u2_chk(''argument_number'', @() k9pick(2));'
    'u2_chk(''member_missing'', @() k9shade.purple);'
    'u2_chk(''construct'', @() k9shade());'
    'u2_chk(''props_member'', @() k9size.medium.Width);'
    'u2_chk(''props_method'', @() doubled(k9size.large));'
    'u2_chk(''props_char'', @() char(k9size.small));'
    'u2_chk(''props_display'', @() k9show(''k9size.small''));'
    'u2_chk(''props_eq'', @() k9size.small == k9size.small);'
    'u2_chk(''props_set'', @() k9setwidth(k9size.small));'
    'u2_chk(''double'', @() double(g));'
    'u2_chk(''mat2str'', @() mat2str(g));'
    'k9divchk(''sort'', @() char(sort([k9shade.blue k9shade.red])));'
    'u2_chk(''fieldnames'', @() fieldnames(g));'
    'u2_chk(''properties'', @() properties(g));'
    });
rehash;
k9enumbody;

cd(old);
rmdir(root, 's');

function put(folder, name, lines)
fid = fopen(fullfile(folder, name), 'w');
fprintf(fid, '%s\n', lines{:});
fclose(fid);
end
