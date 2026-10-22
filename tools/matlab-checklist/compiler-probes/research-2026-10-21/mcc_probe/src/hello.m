function hello(varargin)
%HELLO Standalone main: report arguments and deployment state.
fprintf('nargin=%d\n', nargin);
for k = 1:nargin
    a = varargin{k};
    fprintf('arg %d: class=%s size=%s value=[%s]\n', k, class(a), mat2str(size(a)), char(a));
end
fprintf('isdeployed=%d\n', isdeployed);
if isdeployed
    fprintf('ctfroot=%s\n', ctfroot);
end
[maj, mnr] = mcrversion;
fprintf('mcrversion=[%d,%d]\n', maj, mnr);
fprintf('pwd=%s\n', pwd);
fprintf('which addtwo=%s\n', which('addtwo'));
fprintf('which hello=%s\n', which('hello'));
fprintf('mfilename=%s\n', mfilename('fullpath'));
fprintf('matlabroot=%s\n', matlabroot);
fprintf('tempdir=%s\n', tempdir);
fprintf('prefdir=%s\n', prefdir);
fprintf('usejava(jvm)=%d desktop=%d\n', usejava('jvm'), usejava('desktop'));
fprintf('numel(path entries)=%d\n', numel(strsplit(path, pathsep)));
s = load('data.mat');
fprintf('data.mat fields=%s v=%s\n', strjoin(fieldnames(s)', ','), mat2str(s.v));
fprintf('which data.mat=%s\n', which('data.mat'));
fprintf('addtwo(2,3) via direct call:\n');
addtwo(2, 3);
end
