function s = k14struct(in)
%K14STRUCT Describe a struct's fields and answer a struct of its own.
if nargin == 0
    in = struct();
end
s = struct('fields', {fieldnames(in)'}, 'count', numel(fieldnames(in)), 'echo', in);
end
