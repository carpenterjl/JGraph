% oi77_fh - open item 77: how R2025b writes function handles into a level-5 MAT-file (uncompressed,
% -v6), for the figure-file writer to copy: a handle by name, an anonymous one with a captured
% variable, one without, a cell callback. rt\fh.mat is read back by the Python dumper and by
% load, and each handle is called.
here = fileparts(mfilename('fullpath'));
rt = fullfile(here, 'rt');
if ~isfolder(rt), mkdir(rt); end
n = 7;
f1 = @oi77_named;
f2 = @(s, e) disp(sprintf('anon %d', n));
f3 = @(x) x + 1;
c = {@oi77_named, 5};
save(fullfile(rt, 'fh.mat'), 'f1', 'f2', 'f3', 'c', '-v6');
clear f1 f2 f3 c n
s = load(fullfile(rt, 'fh.mat'));
fprintf('loaded: %s\n', strjoin(fieldnames(s)', ','));
s.f1([], []);
s.f2([], []);
fprintf('f3(1) = %g\n', s.f3(1));
fprintf('c: %s %d\n', func2str(s.c{1}), s.c{2});
w = functions(s.f2);
fprintf('f2 functions: type=%s file=%s workspace=%s\n', w.type, w.file, class(w.workspace));
disp(w.workspace{1});
