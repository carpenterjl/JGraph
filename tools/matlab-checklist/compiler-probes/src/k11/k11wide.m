function [o1, o2, o3, o4, varargout] = k11wide(i1, i2, i3, i4, i5, i6, i7, i8, varargin)
%K11WIDE Many inputs and outputs, then both variable lists: nargin, nargout, i1 + i8 and
%numel(varargin), then 1, 2, ... for each further output.
o1 = nargin;
o2 = nargout;
o3 = i1 + i8 + 0 * (i2 + i3 + i4 + i5 + i6 + i7);
o4 = numel(varargin);
for k = 1:nargout - 4
    varargout{k} = k; %#ok<AGROW>
end
end
