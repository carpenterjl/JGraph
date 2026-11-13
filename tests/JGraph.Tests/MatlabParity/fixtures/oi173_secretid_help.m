% record: -noFigureWindows
% Open items 173 and 174: secretID, the secret API's value class - its checks, its class answers, its
% arrays and getSecret where there is no vault - and help reading a code file's comments as R2025b
% prints them - the block's place and end, its % read as a space, a class's documentation lines, a
% method's, a property's and a local function's help, a folder's listing, a P-file's source beside
% it and a P-file alone. Help text is shown with each line ending as '/', spaces kept.

% --- secretID: making one ------------------------------------------------------------------------
s = secretID('kname');
t = secretID("other");
u2_chk('sid_class', @() class(s));
u2_chk('sid_name', @() char(s.Name));
u2_chk('sid_name_class', @() class(s.Name));
u2_chk('sid_name_size', @() size(s.Name));
u2_chk('sid_from_string', @() char(t.Name));
u2_chk('sid_empty_string', @() size(char(secretID("").Name)));
u2_chk('sid_space', @() double(char(secretID(' ').Name)));
u2_chk('sid_empty_char', @() secretID(''));
u2_chk('sid_char_1x0', @() secretID(char(zeros(1, 0))));
u2_chk('sid_number', @() secretID(5));
u2_chk('sid_logical', @() secretID(true));
u2_chk('sid_int8', @() secretID(int8(1)));
u2_chk('sid_struct', @() secretID(struct('a', 1)));
u2_chk('sid_handle', @() secretID(@sin));
u2_chk('sid_cell_text', @() secretID({'a'}));
u2_chk('sid_cell_number', @() secretID({1}));
u2_chk('sid_cell_empty', @() secretID({}));
u2_chk('sid_strings', @() secretID(["a" "b"]));
u2_chk('sid_string_0x0', @() secretID(strings(0)));
u2_chk('sid_missing', @() secretID(string(missing)));
u2_chk('sid_charmatrix', @() secretID(['ab'; 'cd']));
u2_chk('sid_column_char', @() secretID(['a'; 'b']));
u2_chk('sid_none', @() secretID());
u2_chk('sid_two', @() secretID('a', 'b'));
chkdiv('sid_object', @() errtext(@() secretID(secretID('a'))));
u2_chk('sid_feval', @() char(feval('secretID', 'f').Name));
u2_chk('sid_func2str', @() func2str(@secretID));

% --- secretID: what it is ------------------------------------------------------------------------
u2_chk('sid_isa', @() isa(s, 'secretID'));
u2_chk('sid_isa_customdisplay', @() isa(s, 'matlab.mixin.CustomDisplay'));
u2_chk('sid_isa_compact', @() isa(s, 'matlab.mixin.CustomCompactDisplayProvider'));
u2_chk('sid_isa_handle', @() isa(s, 'handle'));
u2_chk('sid_isobject', @() isobject(s));
u2_chk('sid_superclasses', @() strjoin(superclasses('secretID'), ','));
u2_chk('sid_superclasses_obj', @() strjoin(superclasses(s), ','));
u2_chk('sid_properties', @() strjoin(properties(s), ','));
u2_chk('sid_properties_class', @() strjoin(properties('secretID'), ','));
u2_chk('sid_methods', @() strjoin(methods(s), ','));
u2_chk('sid_methods_class', @() strjoin(methods('secretID'), ','));
u2_chk('sid_ismethod', @() ismethod(s, 'getSecret'));
u2_chk('sid_isprop', @() isprop(s, 'Name'));
u2_chk('sid_fieldnames', @() strjoin(fieldnames(s), ','));
u2_chk('sid_mc_sealed', @() getfield(?secretID, 'Sealed'));
u2_chk('sid_mc_name', @() getfield(?secretID, 'Name'));
u2_chk('sid_mc_handle', @() getfield(?secretID, 'HandleCompatible'));
u2_chk('sid_exist_class', @() exist('secretID', 'class'));
u2_chk('sid_set_name', @() setname(s));
u2_chk('sid_set_name_dynamic', @() setdyn(s));
u2_chk('sid_subclass', @() subclasstry());
u2_chk('sid_disp', @() ix_flat(dispof(s)));
u2_chk('sid_disp_quote', @() ix_flat(dispof(secretID('it''s "q"'))));
u2_chk('sid_disp_empty_name', @() ix_flat(dispof(secretID(""))));

% --- secretID: arrays, equality, conversions -----------------------------------------------------
u2_chk('sid_array_class', @() class([s t]));
u2_chk('sid_array_size', @() size([s t]));
u2_chk('sid_array_vcat', @() size([s; t]));
u2_chk('sid_array_names', @() char(arrnames([s t])));
u2_chk('sid_array_elem', @() class(second([s t])));
u2_chk('sid_array_grow', @() grow(s, t));
u2_chk('sid_array_disp', @() ix_flat(dispof([s t])));
u2_chk('sid_array_disp_col', @() ix_flat(dispof([s; t])));
u2_chk('sid_repmat', @() size(repmat(s, 2, 3)));
u2_chk('sid_transpose', @() size([s t]'));
u2_chk('sid_empty_static', @() size(secretID.empty));
u2_chk('sid_empty_static_class', @() class(secretID.empty));
u2_chk('sid_empty_disp', @() ix_flat(dispof(secretID.empty)));
u2_chk('sid_isempty', @() isempty(s));
u2_chk('sid_isscalar', @() isscalar(s));
u2_chk('sid_eq', @() s == t);
u2_chk('sid_ne', @() s ~= t);
u2_chk('sid_isequal_same', @() isequal(s, secretID('kname')));
u2_chk('sid_isequal_char_string', @() isequal(secretID('a'), secretID("a")));
u2_chk('sid_isequal_diff', @() isequal(s, t));
u2_chk('sid_copy', @() copyname(s));
u2_chk('sid_string', @() string(s));
u2_chk('sid_char', @() char(s));
u2_chk('sid_cellstr', @() cellstr(s));
u2_chk('sid_double', @() double(s));
u2_chk('sid_sprintf', @() sprintf('%s', s));
u2_chk('sid_sort', @() sort([t s]));
u2_chk('sid_save_load', @() saveload(s));

% --- secretID: getSecret where there is no vault -------------------------------------------------
u2_chk('sid_getSecret', @() getSecret(s));
u2_chk('sid_getSecret_dot', @() s.getSecret());
u2_chk('sid_getSecret_extra', @() getSecret(s, 1));
u2_chk('sid_getSecret_two', @() getSecret([s t]));
u2_chk('sid_getSecret_empty', @() getSecret(secretID.empty));
u2_chk('sid_isSecret_obj', @() isSecret(s));
u2_chk('sid_annotation', @() [class(annotation(s)) ' ' mat2str(size(annotation(s))) ' ' char(strjoin(annotation(s), ','))]);
u2_chk('sid_annotation_col', @() [class(annotation([s; t])) ' ' mat2str(size(annotation([s; t]))) ' ' char(strjoin(annotation([s; t]), ','))]);

% --- help: the files -----------------------------------------------------------------------------
root = tempname;
mkdir(root);
put(root, 'hfafter.m', {'function r = hfafter(x)', '%HFAFTER The H1 line of hfafter.', '%   More text about hfafter.', '%', '%   See also hfbefore, plot.', 'r = x;', '% a later comment', 'end'});
put(root, 'hfbefore.m', {'%HFBEFORE The help before the function line.', '%   Second line.', 'function r = hfbefore(x)', '% the comment after', 'r = x;', 'end'});
put(root, 'hfblank.m', {'function hfblank', '% first block line', '% second block line', '', '% another block after a blank', 'end'});
put(root, 'hfblock.m', {'function hfblock', '%{', 'A block comment line.', '   Indented line.', '%}', 'end'});
put(root, 'hfindent.m', {'function hfindent', '    % indented one', '      %indented two', 'end'});
put(root, 'hfpercents.m', {'function hfpercents', '%no space here', '%%two percents', '%%% three percents', '%  % inner percent', '%', 'end'});
put(root, 'hftab.m', {'function hftab', sprintf('%%\tTabbed line'), 'end'});
put(root, 'hfgap.m', {'function hfgap', '', '% after a blank line', 'end'});
put(root, 'hfcont.m', {'function r = hfcont(a, ...', '    b)', '% help after a continued header', 'r = a + b;', 'end'});
put(root, 'hftail.m', {'function hftail % a comment on the function line', '% the next line', 'end'});
put(root, 'hfwhite.m', {'function hfwhite', '% first', '  ', '% after a line of spaces', 'end'});
put(root, 'hfnone.m', {'function [a, b] = hfnone(x, y)', 'a = x; b = y;', 'end'});
put(root, 'hfnoargs.m', {'function hfnoargs', 'end'});
put(root, 'hfcode.m', {'function hfcode', 'x = 1; % trailing comment', '% later comment', 'end'});
put(root, 'hfcrlf.m', {['function hfcrlf' char(13)], ['% crlf help line' char(13)], 'end'});
put(root, 'hfargs.m', {'function hfargs(x)', '% help before arguments', 'arguments', '    x (1,1) double', 'end', 'end'});
put(root, 'hflocal.m', {'function hflocal', '% main help', 'end', 'function inner', '% inner help', 'end', 'function r = sub2(a, b)', 'r = a;', 'end'});
put(root, 'hscomment.m', {'% A script''s help line.', '%   Its second line.', 'hscomment_v = 1;'});
put(root, 'hsnone.m', {'hsnone_v = 1;', '% comment after code'});
put(root, 'hsblankfirst.m', {'', '', '% script help after blank lines', 'x = 1;'});
put(root, 'hcls.m', {'classdef hcls', '%HCLS The class help line.', '%   More class help.', '    properties', '        P = 1 % the P property', ...
    '        B = 2', '        % comment above C', '        C', '        D = 4 % trailing on D', '    end', '    properties (Constant)', '        K = 3 % the constant', '    end', ...
    '    methods', '        function o = hcls(v)', '            %HCLS The constructor help.', '            o.P = v;', '        end', ...
    '        function r = twice(o)', '            %TWICE Doubles P.', '            r = 2 * o.P;', '        end', ...
    '        function r = bare(o)', '            r = o.P;', '        end', '    end', ...
    '    methods (Static)', '        function r = stat(x)', '            % static help', '            r = x;', '        end', ...
    '        function r = statbare(x)', '            r = x;', '        end', '    end', 'end'});
put(root, 'hclsbefore.m', {'%HCLSBEFORE Help before classdef.', 'classdef hclsbefore', '    properties', '        Q', '    end', 'end'});
put(root, 'hclsnone.m', {'classdef hclsnone', '    properties', '        Q', '    end', 'end'});
mkdir(fullfile(root, 'hfolder'));
put(fullfile(root, 'hfolder'), 'alpha.m', {'function alpha', '%ALPHA The alpha function.', 'end'});
put(fullfile(root, 'hfolder'), 'b.m', {'function b', 'end'});
put(fullfile(root, 'hfolder'), 'Gamma.m', {'% GAMMA script with help', 'x = 1;'});
put(fullfile(root, 'hfolder'), 'dscript.m', {'x = 1;'});
put(fullfile(root, 'hfolder'), 'hfc.m', {'classdef hfc', '%HFC a class', 'end'});
put(fullfile(root, 'hfolder'), 'nolead.m', {'function nolead', '%nolead lower case h1 here', 'end'});
put(fullfile(root, 'hfolder'), 'other.m', {'function other', '% something else first', 'end'});
put(fullfile(root, 'hfolder'), 'notes.txt', {'text'});
mkdir(fullfile(root, 'hcontents'));
put(fullfile(root, 'hcontents'), 'Contents.m', {'% HCONTENTS folder contents', '%   inhc - a function'});
put(fullfile(root, 'hcontents'), 'inhc.m', {'function inhc', '%INHC In the contents folder.', 'end'});
mkdir(fullfile(root, 'hempty'));
put(root, 'hpboth.m', {'function r = hpboth(x)', '%HPBOTH Help of the P-file''s source.', '%   Second line.', 'r = x;', 'end'});
put(root, 'hponly.m', {'function r = hponly(x)', '%HPONLY Help that goes away.', 'r = x;', 'end'});
put(root, 'hpscript.m', {'% script P help', 'x = 1;'});
put(root, 'hpcls.m', {'classdef hpcls', '%HPCLS class P help', 'end'});
put(root, 'hpclsonly.m', {'classdef hpclsonly', '%HPCLSONLY gone', 'end'});
was = cd(root);
pcode('hpboth.m');
pcode('hponly.m');
delete('hponly.m');
pcode('hpscript.m');
delete('hpscript.m');
pcode('hpcls.m');
pcode('hpclsonly.m');
delete('hpclsonly.m');
addpath(fullfile(root, 'hfolder'));
addpath(fullfile(root, 'hempty'));
rehash

% --- help: what it answers -----------------------------------------------------------------------
names = {'hfafter', 'hfbefore', 'hfblank', 'hfblock', 'hfindent', 'hfpercents', 'hftab', 'hfgap', 'hfcont', 'hftail', 'hfwhite', ...
    'hfnone', 'hfnoargs', 'hfcode', 'hfcrlf', 'hfargs', 'hflocal', 'hflocal>inner', 'hflocal>sub2', 'hflocal>nosuch', ...
    'hscomment', 'hsnone', 'hsblankfirst', 'hcls', 'hcls.twice', 'hcls/twice', 'hcls.bare', 'hcls/bare', 'hcls.stat', 'hcls.statbare', ...
    'hcls.hcls', 'hcls/hcls', 'hcls.P', 'hcls.B', 'hcls.C', 'hcls.D', 'hcls.K', 'hcls/P', 'hcls.nosuch', 'hclsbefore', 'hclsnone', ...
    'hfolder', 'hcontents', 'hempty', 'alpha', 'hfolder/alpha', 'hpboth', 'hponly', 'hpscript', 'hpcls', 'hpclsonly', ...
    'hfafter.m', 'hpboth.p', 'hfolder/alpha.m', 'nosuchthing_k'};
for i = 1:numel(names)
    hchk(['help_' strrep(strrep(strrep(names{i}, '.', '_dot_'), '/', '_slash_'), '>', '_gt_')], @() help(names{i}));
end
u2_chk('help_class_of_text', @() class(help('hfafter')));
hchk('help_string_arg', @() (help("hfbefore")));
hchk('help_wrong_case', @() help('HFLOCAL'));
u2_chk('help_size_none', @() size(help('nosuchthing_k')));

% --- help: what it prints ------------------------------------------------------------------------
% (R2025b's evalc keeps its hotlinks and bolds an H1 line's name in lower case; -batch prints the text as
% help returns it and a blank line, which is what this build prints, so only texts without an H1 line
% are compared here.)
hchk('print_hfnone', @() (strip_links(evalc('help hfnone'))));
hchk('print_hponly', @() (strip_links(evalc('help hponly'))));
cd(was);
rmpath(fullfile(root, 'hfolder'));
rmpath(fullfile(root, 'hempty'));
rmdir(root, 's');

function hchk(name, fn)
% One CHK line for a help text, its spaces kept; an error as its identifier.
try
    fprintf('CHK|%s|%s|exact\n', name, flat(fn()));
catch err
    fprintf('CHK|%s|ERR %s|exact\n', name, err.identifier);
end
end

function s = flat(t)
% Help text on one line: each line ending as '/', tabs as '\t', bars as '!'; spaces kept.
s = strrep(strrep(strrep(char(t), newline, '/'), sprintf('\t'), '\t'), '|', '!');
end

function t = strip_links(t)
% R2025b's evalc keeps the help's bold names and links; their text is what -batch prints.
t = regexprep(t, '<a [^>]*>([^<]*)</a>', '$1');
t = regexprep(t, '</?strong>', '');
end

function t = dispof(v)
t = evalc('disp(v)');
end

function r = setname(s)
s.Name = "b";
r = s.Name;
end

function r = setdyn(s)
s.('Name') = "b";
r = s.Name;
end

function r = arrnames(a)
r = strjoin([a.Name], ',');
end

function r = second(a)
r = a(2);
end

function r = grow(s, t)
a = s;
a(3) = t;
r = size(a);
end

function r = copyname(s)
c = s;
r = isequal(c, s) && c.Name == s.Name;
end

function r = subclasstry()
d = tempname;
mkdir(d);
fid = fopen(fullfile(d, 'ksubsecret.m'), 'w');
fprintf(fid, 'classdef ksubsecret < secretID\nend\n');
fclose(fid);
addpath(d);
c = onCleanup(@() rmpath(d));
o = ksubsecret('x');
r = class(o);
end

function r = saveload(s)
f = [tempname '.mat'];
save(f, 's');
l = load(f);
delete(f);
r = [class(l.s) ' ' char(l.s.Name)];
end

function s = errtext(f)
try
    f();
    s = 'no error';
catch e
    s = [e.identifier ' / ' e.message];
end
s = strrep(s, newline, ' ');
end

function put(folder, name, lines)
fid = fopen(fullfile(folder, name), 'w');
fprintf(fid, '%s\n', lines{:});
fclose(fid);
end

function chkdiv(name, fn)
% A u2_chk line whose rule is a recorded divergence: the stamp adds what JGraph prints.
fprintf('CHK|%s|%s|div=ADR0348\n', name, u2_text(fn()));
end
