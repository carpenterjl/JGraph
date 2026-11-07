function [first, varargout] = k17_both(x, varargin)
%K17_BOTH A fixed input and output before varargin and varargout (stage K17).
first = sprintf('x=%s nargin=%d nargout=%d', k17_show(x), nargin, nargout);
for k = 1:max(nargout - 1, 0)
    if k <= numel(varargin)
        varargout{k} = varargin{k}; %#ok<AGROW>
    else
        varargout{k} = k; %#ok<AGROW>
    end
end
end
