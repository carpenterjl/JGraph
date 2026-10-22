function k0_args(varargin)
%K0_ARGS Report how a standalone receives its arguments (built twice: plain and with -n).
k0_quiet;
fprintf('nargin\t%d\n', nargin);
for k = 1:nargin
    fprintf('arg%d\t%s\n', k, k0_show(varargin{k}));
end
fprintf('isdeployed\t%d\n', isdeployed);
end
