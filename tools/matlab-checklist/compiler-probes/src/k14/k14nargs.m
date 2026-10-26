function varargout = k14nargs(varargin)
%K14NARGS Answer how many inputs and outputs the call had, in each output.
for k = 1:max(nargout, 1)
    varargout{k} = [nargin nargout];
end
end
