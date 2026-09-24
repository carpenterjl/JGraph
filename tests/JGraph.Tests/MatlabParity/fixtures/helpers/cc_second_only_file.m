function [a, b] = cc_second_only_file() %#ok<STOUT>
% A function in a file of its own that leaves its first output unassigned (V9, ADR 0170): the
% refusal names the file, not file>function.
b = 2;
end
