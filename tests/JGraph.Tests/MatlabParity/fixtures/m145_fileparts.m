% m145_fileparts.m -- fileparts is a textual split: [folder, name, ext] = fileparts(p), and one
% output is the folder alone (it used to be a three-cell here, found while fixing run under
% M145). Measured in R2025b: the folder is everything before the last separator, either slash,
% keeping the separator only for a root; the extension starts at the last dot of the rest; a
% trailing separator leaves name and extension empty; strings answer strings, cellstrs cells.

cases = {'C:\a\b\c.txt', 'a/b/c.tar.gz', 'c.txt', '.gitignore', 'a\b\', 'a/b/', '', ...
    'a\b\c', 'C:\', 'C:\a', 'a.b/c', 'a/b/.', 'a/b/..', 'x.', 'a/b/c.txt/', '/x/y.m', '/y.m'};
for k = 1:numel(cases)
    p = cases{k};
    [f, n, e] = fileparts(p);
    chk(sprintf('three_%02d', k), ['[' f '][' n '][' e ']']);
    chk(sprintf('one_%02d', k), ['[' fileparts(p) ']']);
end

[f, n, e] = fileparts("a/b/c.txt");
chk('string_class', [class(f) ' ' class(n) ' ' class(e)]);
chk('string_parts', ['[' char(f) '][' char(n) '][' char(e) ']']);
one = fileparts("a/b/c.txt");
chk('string_one', [class(one) ' [' char(one) ']']);

[f, n, e] = fileparts({'a/b.txt', 'c/d/e'});
chk('cell_class', [class(f) ' ' class(n) ' ' class(e)]);
chk('cell_parts', ['[' f{1} '][' n{1} '][' e{1} '] [' f{2} '][' n{2} '][' e{2} ']']);
chk('cell_one', class(fileparts({'a/b.txt'})));

chk('number_refused', refused(@() fileparts(3)));
chk('joined_back', fullfile(fileparts('one/two/three.txt'), 'x'));

% fullfile is text too: the platform separator, slashes converted on Windows, runs collapsed
% except a leading pair (a UNC share), empty parts dropped, a trailing separator kept.
chk('fullfile_slashes', fullfile('a/b', 'c'));
chk('fullfile_runs', fullfile('a//b', 'c/'));
chk('fullfile_unc', fullfile('//srv/share', 'f'));
chk('fullfile_empty_parts', fullfile('a', '', 'x'));
chk('fullfile_leading_empty', fullfile('', 'x'));
chk('fullfile_trailing', fullfile('a/b/', ''));
chk('fullfile_dots', fullfile('a/../b', 'x'));
chk('fullfile_one', fullfile('a'));
chk('fullfile_string', class(fullfile("a", 'b')));
chk('fullfile_string_text', char(fullfile("a", 'b')));

% A cellstr or string array part joins element by element (measured in R2025b): every container
% must share a shape or be a scalar, which repeats; the answer is a string when any part is a
% string and a cell otherwise; no arguments at all is refused.
c = fullfile({'a', 'b'}, 'c');
chk('cell_char_class', [class(c) ' ' mat2str(size(c))]);
chk('cell_char_text', [c{1} ' ' c{2}]);
c = fullfile('r', {'a'; 'b'});
chk('char_cell_col', [mat2str(size(c)) ' ' c{1} ' ' c{2}]);
c = fullfile({'a', 'b'}, {'x', 'y'});
chk('cell_cell', [c{1} ' ' c{2}]);
c = fullfile({'a', 'b'}, {'x'});
chk('cell_one_repeats', [c{1} ' ' c{2}]);
c = fullfile({'a', ''}, 'x');
chk('cell_empty_elem', ['[' c{1} '] [' c{2} ']']);
c = fullfile({'a'}, 'x');
chk('cell_one_class', [class(c) ' ' mat2str(size(c)) ' ' c{1}]);
c = fullfile({}, 'x');
chk('cell_empty', [class(c) ' ' mat2str(size(c))]);
s = fullfile(["a", "b"], 'c');
chk('strarr_class', [class(s) ' ' mat2str(size(s))]);
chk('strarr_text', [char(s(1)) ' ' char(s(2))]);
s = fullfile({'a', 'b'}, "s");
chk('cell_string', [class(s) ' ' char(s(1)) ' ' char(s(2))]);
s = fullfile(["a", "b"], {'x', 'y'});
chk('strarr_cell', [class(s) ' ' char(s(1)) ' ' char(s(2))]);
chk('mismatch_refused', refused(@() fullfile({'a', 'b'}, {'x', 'y', 'z'})));
chk('row_col_refused', refused(@() fullfile({'a', 'b'}, {'x'; 'y'})));
chk('noargs_refused', refused(@() fullfile()));

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

function no = refused(call)
no = false;
try
    call();
catch
    no = true;
end
end
