function [count, last] = k8args(first, second, varargin)
%K8ARGS Count the arguments given; the second output is the last argument.
count = sprintf('nargin=%d nargout=%d', nargin, nargout);
if nargin == 0
    last = [];
elseif nargin == 1
    last = first;
elseif nargin == 2
    last = second;
else
    last = varargin{end};
end
end
