function varargout = k10nargs(varargin)
%K10NARGS Answer nargin and nargout, one output per output asked for.
for k = 1:max(nargout, 1)
    varargout{k} = [nargin nargout k]; %#ok<AGROW>
end
end
