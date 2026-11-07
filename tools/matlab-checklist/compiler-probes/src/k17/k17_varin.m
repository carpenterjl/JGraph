function r = k17_varin(varargin)
%K17_VARIN Takes any number of inputs and describes each (stage K17).
parts = cellfun(@k17_show, varargin, 'UniformOutput', false);
r = sprintf('nargin=%d {%s}', nargin, strjoin(parts, '; '));
end
