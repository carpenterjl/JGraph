function [a, varargout] = k10mixed(x, varargin)
%K10MIXED A fixed output and input before the variable ones: answers nargin and nargout, then
%x times 1, 2, ... for each further output asked for.
a = [nargin nargout numel(varargin)];
for k = 1:nargout - 1
    varargout{k} = x * k; %#ok<AGROW>
end
end
