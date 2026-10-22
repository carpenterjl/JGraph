function [s, p, msg] = multi(varargin)
%MULTI Sum, product and a message over any number of numeric inputs.
s = 0; p = 1;
for k = 1:nargin
    s = s + sum(varargin{k}(:));
    p = p * prod(varargin{k}(:));
end
msg = sprintf('multi got %d inputs, nargout=%d', nargin, nargout);
end
