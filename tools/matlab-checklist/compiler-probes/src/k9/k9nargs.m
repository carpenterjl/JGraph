function r = k9nargs(varargin)
%K9NARGS How many inputs arrived and what they were.
parts = cellfun(@k9describe, varargin, 'UniformOutput', false);
r = sprintf('%d: %s', nargin, strjoin(parts, ' | '));
end
