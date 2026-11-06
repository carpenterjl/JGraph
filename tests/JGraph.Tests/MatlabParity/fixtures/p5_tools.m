% p5_tools.m -- the packages and class folders plan's stage P5 (ADR 0311): the Code Analyzer over
% files in +package and @Class folders and over files whose function or class is not named after
% the file -- R2025b's FNDEF, FNDOT, MCFIL, MCDIR and NOIN with their spans, severities and fixes --
% the name forms checkcode and codeIssues take for such files (Thing/extent, +pkg/g, @Thing/extent,
% a bare method name; pkg.f refused), codeIssues' walk of a folder holding +, @ and private folders,
% fix over the file-name checks, help and type over a class folder's method, and edit headless,
% which opens nothing and says nothing. Every rule is R2025b's as recorded here; the probes
% (tools/matlab-checklist/ui-probes/p5) settled them first.
%
% A fixture is one file, so this one builds the folders it needs under tempdir -- a current folder
% holding the plain files and a library holding the rest -- and removes them at the end. A name with
% no file is never given to edit: R2025b asks whether to create it, even in -batch.

start = pwd;
root = fullfile(tempdir, 'p5_tools');
if exist(root, 'dir') == 7
    rmdir(root, 's');
end
cur = fullfile(root, 'cur');
lib = fullfile(root, 'lib');
mkdir(cur);
mkdir(lib);
warning('off', 'backtrace');

% ---- plain files in the current folder ----
put(fullfile(cur, 'misnamed.m'), {'function r = othername(x)', 'r = x;', 'end'});
put(fullfile(cur, 'casename.m'), {'function r = Casename(x)', 'r = x;', 'end'});
put(fullfile(cur, 'noargs.m'), {'function othernoargs', 'disp(1)', 'end'});
put(fullfile(cur, 'twoout.m'), {'function [a, b] = othertwo(x)', 'a = x;', 'b = x;', 'end'});
put(fullfile(cur, 'withlocal.m'), {'function r = otherlocal(x)', 'r = helper(x);', 'end', 'function y = helper(z)', 'y = z;', 'end'});
put(fullfile(cur, 'nested.m'), {'function r = othernested(x)', 'r = inner(x);', '    function y = inner(z)', '    y = z;', '    end', 'end'});
put(fullfile(cur, 'dotted.m'), {'function r = pkg.dotted(x)', 'r = x;', 'end'});
put(fullfile(cur, 'commented.m'), {'% a comment first', '', 'function r = othercommented(x)', 'r = x;', 'end'});
put(fullfile(cur, 'suppressed.m'), {'function r = othersupp(x) %#ok<FNDEF>', 'r = x;', 'end'});
put(fullfile(cur, 'Wrongclass.m'), {'classdef Other', 'end'});
put(fullfile(cur, 'Wrongclass2.m'), {'classdef (Sealed) Other2 < handle', '    properties', '        A', '    end', 'end'});
put(fullfile(cur, 'Wrongctor.m'), {'classdef Otherctor', '    methods', '        function o = Otherctor()', '            disp(o)', '        end', '    end', 'end'});
put(fullfile(cur, 'Sig.m'), {'classdef Sig', '    methods', '        r = m(o)', '        r = n(o, k)', '    end', 'end'});
put(fullfile(cur, 'Noin.m'), {'classdef Noin', '    methods', '        function bodied', '            disp(1)', '        end', '        function r = bodiedout', '            r = 1;', '        end', '        sigline', '        r = sigout', '        function withtilde(~)', '            disp(2)', '        end', '    end', '    methods (Static)', '        function s()', '            disp(3)', '        end', '        t', '    end', '    methods (Abstract)', '        a', '    end', '    methods (Access = private)', '        function p', '            disp(4)', '        end', '    end', 'end'});
put(fullfile(cur, 'scriptfile.m'), {'x = 1;', 'disp(x)'});
put(fullfile(cur, 'good.m'), {'function r = good(x)', 'r = x;', 'end'});

% ---- the library: a class folder, a misnamed class folder, a folder without its class, a package ----
put(fullfile(lib, '@P5Thing', 'P5Thing.m'), {'classdef P5Thing', '    properties', '        W = 1', '    end', '    methods', '        function o = P5Thing(w)', '            if nargin > 0, o.W = w; end', '        end', '        r = extent(o)', '        r = scale(o, k)', '        [a, b] = two(o)', '        noout(o)', '        bare', '        function r = inline1(o)', '            r = o.W;', '        end', '    end', '    methods (Static)', '        r = staticOne(x)', '    end', '    methods (Access = private)', '        r = secret(o)', '    end', 'end'});
put(fullfile(lib, '@P5Thing', 'extent.m'), {'function r = extent(o)', '% EXTENT  twice the width', 'r = o.W * 2;', 'end'});
put(fullfile(lib, '@P5Thing', 'scale.m'), {'function r = scale(o, k)', 'r = o.W * k;', 'end'});
put(fullfile(lib, '@P5Thing', 'misnamed.m'), {'function r = othername(o)', 'r = o.W;', 'end'});
put(fullfile(lib, '@P5Thing', 'undeclared.m'), {'function r = undeclared(o)', 'r = o.W;', 'end'});
put(fullfile(lib, '@P5Thing', 'staticOne.m'), {'function r = staticOne(x)', 'r = x;', 'end'});
put(fullfile(lib, '@P5Thing', 'private', 'p5hidden.m'), {'function r = p5hidden(o)', 'r = o.W;', 'end'});
put(fullfile(lib, '@P5Thing', 'private', 'misprivate.m'), {'function r = otherprivate(o)', 'r = o.W;', 'end'});
put(fullfile(lib, '@P5Mis', 'P5Mis.m'), {'classdef Wrong', 'end'});
put(fullfile(lib, '@P5Lone', 'method1.m'), {'function r = other1(o)', 'r = o;', 'end'});
put(fullfile(lib, '+p5pkg', 'f.m'), {'function r = f(x)', 'r = x * 10;', 'end'});
put(fullfile(lib, '+p5pkg', 'g.m'), {'function r = g(x)', 'r = f(x);', 'end'});
put(fullfile(lib, '+p5pkg', 'misnamed.m'), {'function r = othername(x)', 'r = x;', 'end'});
put(fullfile(lib, '+p5pkg', 'C.m'), {'classdef C', '    methods (Static)', '        function r = stat(x)', '            r = x;', '        end', '    end', 'end'});
put(fullfile(lib, '+p5pkg', 'Wrongc.m'), {'classdef C', 'end'});
put(fullfile(lib, '+p5pkg', 's.m'), {'disp(1)'});
put(fullfile(lib, '+p5pkg', '@D', 'D.m'), {'classdef D', '    methods', '        r = dm(o)', '    end', 'end'});
put(fullfile(lib, '+p5pkg', '@D', 'dm.m'), {'function r = dm(o)', 'r = o;', 'end'});
put(fullfile(lib, '+p5pkg', '@D', 'wrongdm.m'), {'function r = otherdm(o)', 'r = o;', 'end'});
put(fullfile(lib, '+p5pkg', 'private', 'ph.m'), {'function r = ph(x)', 'r = x;', 'end'});
put(fullfile(lib, '+p5pkg', 'private', 'wrongph.m'), {'function r = otherph(x)', 'r = x;', 'end'});
put(fullfile(lib, 'P5Both.m'), {'function r = P5Both(x)', 'r = x;', 'end'});
put(fullfile(lib, 'sub', 'plainsub.m'), {'function r = othersub(x)', 'r = x;', 'end'});

cd(cur);
addpath(lib);
rel = @(p) strrep(p, root, '<root>');
firstline = @(t) strtok(t, newline);
oneline = @(t) strrep(strrep(strtrim(t), char([13 10]), '/'), newline, '/');
relall = @(c) strjoin(cellfun(rel, c, 'UniformOutput', false), ';');

% ---- the messages over every file: id, line, columns, fix and text ----
chk('an_misnamed', msgs(fullfile(cur, 'misnamed.m'), rel));
chk('an_casename', msgs(fullfile(cur, 'casename.m'), rel));
chk('an_noargs', msgs(fullfile(cur, 'noargs.m'), rel));
chk('an_twoout', msgs(fullfile(cur, 'twoout.m'), rel));
chk('an_withlocal', msgs(fullfile(cur, 'withlocal.m'), rel));
chk('an_nested', msgs(fullfile(cur, 'nested.m'), rel));
chk('an_dotted', msgs(fullfile(cur, 'dotted.m'), rel));
chk('an_commented', msgs(fullfile(cur, 'commented.m'), rel));
chk('an_suppressed', msgs(fullfile(cur, 'suppressed.m'), rel));
chk('an_suppressed_notok', msgsopt(fullfile(cur, 'suppressed.m'), '-notok', rel));
chk('an_wrongclass', msgs(fullfile(cur, 'Wrongclass.m'), rel));
chk('an_wrongclass2', msgs(fullfile(cur, 'Wrongclass2.m'), rel));
chk('an_wrongctor', msgs(fullfile(cur, 'Wrongctor.m'), rel));
chk('an_sig', msgs(fullfile(cur, 'Sig.m'), rel));
chk('an_noin', msgs(fullfile(cur, 'Noin.m'), rel));
chk('an_scriptfile', msgs(fullfile(cur, 'scriptfile.m'), rel));
chk('an_good', msgs(fullfile(cur, 'good.m'), rel));
chk('an_thing_class', msgs(fullfile(lib, '@P5Thing', 'P5Thing.m'), rel));
chk('an_thing_extent', msgs(fullfile(lib, '@P5Thing', 'extent.m'), rel));
chk('an_thing_misnamed', msgs(fullfile(lib, '@P5Thing', 'misnamed.m'), rel));
chk('an_thing_undeclared', msgs(fullfile(lib, '@P5Thing', 'undeclared.m'), rel));
chk('an_thing_private', msgs(fullfile(lib, '@P5Thing', 'private', 'p5hidden.m'), rel));
chk('an_thing_misprivate', msgs(fullfile(lib, '@P5Thing', 'private', 'misprivate.m'), rel));
chk('an_mis_class', msgs(fullfile(lib, '@P5Mis', 'P5Mis.m'), rel));
chk('an_lone_method', msgs(fullfile(lib, '@P5Lone', 'method1.m'), rel));
chk('an_pkg_f', msgs(fullfile(lib, '+p5pkg', 'f.m'), rel));
chk('an_pkg_g', msgs(fullfile(lib, '+p5pkg', 'g.m'), rel));
chk('an_pkg_misnamed', msgs(fullfile(lib, '+p5pkg', 'misnamed.m'), rel));
chk('an_pkg_c', msgs(fullfile(lib, '+p5pkg', 'C.m'), rel));
chk('an_pkg_wrongc', msgs(fullfile(lib, '+p5pkg', 'Wrongc.m'), rel));
chk('an_pkg_s', msgs(fullfile(lib, '+p5pkg', 's.m'), rel));
chk('an_pkg_d', msgs(fullfile(lib, '+p5pkg', '@D', 'D.m'), rel));
chk('an_pkg_dm', msgs(fullfile(lib, '+p5pkg', '@D', 'dm.m'), rel));
chk('an_pkg_wrongdm', msgs(fullfile(lib, '+p5pkg', '@D', 'wrongdm.m'), rel));
chk('an_pkg_ph', msgs(fullfile(lib, '+p5pkg', 'private', 'ph.m'), rel));
chk('an_pkg_wrongph', msgs(fullfile(lib, '+p5pkg', 'private', 'wrongph.m'), rel));
chk('an_both', msgs(fullfile(lib, 'P5Both.m'), rel));

% ---- severities ----
chk('sev_misnamed', sevs(fullfile(cur, 'misnamed.m')));
chk('sev_dotted', sevs(fullfile(cur, 'dotted.m')));
chk('sev_wrongclass', sevs(fullfile(cur, 'Wrongclass.m')));
chk('sev_noin', sevs(fullfile(cur, 'Noin.m')));
chk('sev_mis', sevs(fullfile(lib, '@P5Mis', 'P5Mis.m')));

% ---- the string forms ----
chk('str_misnamed', oneline(checkcode(fullfile(cur, 'misnamed.m'), '-string')));
chk('str_misnamed_id', oneline(checkcode(fullfile(cur, 'misnamed.m'), '-string', '-id')));
chk('str_misnamed_cyc', oneline(checkcode(fullfile(cur, 'misnamed.m'), '-string', '-cyc')));
chk('str_wrongclass', oneline(checkcode(fullfile(cur, 'Wrongclass.m'), '-string')));
chk('str_noin', oneline(checkcode(fullfile(cur, 'Noin.m'), '-string', '-id')));
chk('str_mis', oneline(rel(checkcode(fullfile(lib, '@P5Mis', 'P5Mis.m'), '-string', '-id'))));

% ---- the name forms (the current folder holds the plain files, the library is on the path) ----
chk('name_slash', named('P5Thing/extent', rel));
chk('name_class', named('P5Thing', rel));
chk('name_bare_method', named('extent', rel));
chk('name_cur_first', named('misnamed', rel));
chk('name_relative_pkg', named('+p5pkg/g', rel));
chk('name_relative_at', named('@P5Thing/extent', rel));
chk('name_relative_at_ext', named('@P5Thing/extent.m', rel));
chk('name_relative_class', named('@P5Mis/P5Mis', rel));
chk('name_dotted_id', idof(@() checkcode('p5pkg.f')));
chk('name_dotted_msg', msgof(@() checkcode('p5pkg.f')));
chk('name_dotted_class_id', idof(@() checkcode('p5pkg.C')));
chk('name_dotted_method_id', idof(@() checkcode('P5Thing.extent')));
chk('name_function_id', idof(@() checkcode('othername')));
chk('name_no_plus_id', idof(@() checkcode('p5pkg/g')));
chk('name_no_plus_msg', msgof(@() checkcode('p5pkg/g')));
chk('name_slash_nosuch_id', idof(@() checkcode('P5Thing/nosuch')));
chk('name_two_files', relall(secondof({'+p5pkg/g.m', '@P5Thing/extent.m'})));
chk('issues_dotted_id', idof(@() codeIssues('p5pkg.f')));
chk('issues_dotted_msg', msgof(@() codeIssues('p5pkg.f')));
chk('issues_slash_id', idof(@() codeIssues('P5Thing/extent')));
chk('issues_slash_msg', msgof(@() codeIssues('P5Thing/extent')));
chk('issues_relative_id', idof(@() codeIssues('@P5Thing/extent.m')));
q = codeIssues('misnamed');
chk('issues_plain', relall(cellstr(q.Files)'));
cd(lib);
chk('name_in_lib_relative', named('+p5pkg/g', rel));
chk('name_in_lib_at', named('@P5Thing/misnamed', rel));
chk('name_in_lib_slash', named('P5Thing/extent', rel));
chk('name_in_lib_both', named('P5Both', rel));
cd(cur);

% ---- codeIssues over the library: the walk into +, @ and private folders ----
q = codeIssues(lib);
chk('issues_lib_files', relall(cellstr(q.Files)'));
chk('issues_lib_rows', issuerows(q, rel));
q = codeIssues(lib, 'IncludeSubfolders', true);
chk('issues_lib_all_files', relall(cellstr(q.Files)'));
q = codeIssues(fullfile(lib, '@P5Thing'));
chk('issues_thing_files', relall(cellstr(q.Files)'));
q = codeIssues(fullfile(lib, '+p5pkg'));
chk('issues_pkg_files', relall(cellstr(q.Files)'));
q = codeIssues(fullfile(cur, 'misnamed.m'));
chk('issues_misnamed_severity', char(string(q.Issues.Severity(1))));
chk('issues_misnamed_fixability', char(string(q.Issues.Fixability(1))));
chk('issues_misnamed_checkid', char(string(q.Issues.CheckID(1))));
chk('issues_misnamed_columns', sprintf('%d %d %d %d', q.Issues.LineStart(1), q.Issues.LineEnd(1), q.Issues.ColumnStart(1), q.Issues.ColumnEnd(1)));

% ---- fix over the file-name checks ----
fixdir = fullfile(root, 'fix');
mkdir(fixdir);
copyfile(fullfile(cur, 'misnamed.m'), fixdir);
copyfile(fullfile(cur, 'Wrongctor.m'), fixdir);
copyfile(fullfile(cur, 'commented.m'), fixdir);
cfx = codeIssues(fullfile(fixdir, 'misnamed.m'));
[cfx, status] = fix(cfx, 'FNDEF');
chk('fix_fndef_status', sprintf('%d %s %d %d %d %d', status.Success(1), char(string(status.CheckID(1))), status.LineStart(1), status.LineEnd(1), status.ColumnStart(1), status.ColumnEnd(1)));
chk('fix_fndef_issues_left', height(cfx.Issues));
L = readlines(fullfile(fixdir, 'misnamed.m'));
chk('fix_fndef_line1', char(L(1)));
cfx = codeIssues(fullfile(fixdir, 'Wrongctor.m'));
cfx = fix(cfx, 'MCFIL');
L = readlines(fullfile(fixdir, 'Wrongctor.m'));
chk('fix_mcfil_line1', char(L(1)));
chk('fix_mcfil_line3', char(L(3)));
chk('fix_mcfil_issues_left', height(cfx.Issues));
cfx = codeIssues(fullfile(fixdir, 'commented.m'));
cfx = fix(cfx, 'FNDEF');
L = readlines(fullfile(fixdir, 'commented.m'));
chk('fix_fndef_commented_line3', char(L(3)));

% ---- help and type over a class folder's method (P4's forms, kept) ----
chk('help_slash_method', oneline(help('P5Thing/extent')));
chk('type_slash_method', firstline(evalc('type P5Thing/extent')));
chk('type_relative_method', firstline(evalc('type(''@P5Thing/scale.m'')')));

% ---- edit headless: nothing opened, nothing said ----
chk('edit_dotted', evalc('edit(''p5pkg.f'')'));
chk('edit_dotted_command', evalc('edit p5pkg.f'));
chk('edit_slash', evalc('edit(''P5Thing/extent'')'));
chk('edit_class', evalc('edit(''P5Thing'')'));
chk('edit_dotted_method', evalc('edit(''P5Thing.extent'')'));
chk('edit_relative', evalc('edit(''@P5Thing/extent'')'));
chk('edit_file', evalc('edit(''good.m'')'));
chk('edit_plain', evalc('edit(''misnamed'')'));
chk('edit_number_id', idof(@() edit(1)));
chk('edit_number_msg', msgof(@() edit(1)));

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

function s = msgs(file, rel)
s = msgsopt(file, '-id', rel);
end

function s = msgsopt(file, opt, rel)
m = checkcode(file, '-id', '-struct', opt);
if isempty(m)
    s = 'none';
    return
end
parts = cell(1, numel(m));
for k = 1:numel(m)
    parts{k} = sprintf('%s L%d C%d-%d fix%d: %s', m(k).id, m(k).line, m(k).column(1), m(k).column(2), m(k).fix, rel(m(k).message));
end
s = strjoin(parts, ' / ');
end

function s = sevs(file)
m = checkcode(file, '-id', '-struct', '-severity');
parts = cell(1, numel(m));
for k = 1:numel(m)
    parts{k} = sprintf('%s=%d', m(k).id, m(k).severity);
end
s = strjoin(parts, ',');
end

function s = named(name, rel)
[~, files] = checkcode(name);
s = rel(files{1});
end

function files = secondof(names)
[~, files] = checkcode(names);
files = files';
end

function s = issuerows(q, rel)
T = q.Issues;
parts = cell(1, height(T));
for k = 1:height(T)
    parts{k} = sprintf('%s %s L%d C%d-%d', rel(char(T.FullFilename(k))), char(string(T.CheckID(k))), T.LineStart(k), T.ColumnStart(k), T.ColumnEnd(k));
end
s = strjoin(parts, ' / ');
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

function id = idof(call)
id = '';
try
    call();
catch e
    id = e.identifier;
end
end

function m = msgof(call)
m = '';
try
    call();
catch e
    m = e.message;
end
end
