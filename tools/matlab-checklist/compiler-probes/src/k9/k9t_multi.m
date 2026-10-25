function [total, count, label] = k9t_multi(values)
%K9T_MULTI Three typed outputs.
arguments (Input)
    values (1,:) double {mustBeReal}
end
arguments (Output)
    total (1,1) double {mustBeReal}
    count (1,1) int32 {mustBeReal}
    label (1,1) string
end
total = sum(values);
count = int32(numel(values));
label = "n=" + numel(values);
end
