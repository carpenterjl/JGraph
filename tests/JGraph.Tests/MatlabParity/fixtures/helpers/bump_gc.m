function s = bump_gc()
% A file function regexprep's ${...} replacement can call: writes the global cell it is replacing in.
global gc
gc{2} = 'zzz';
s = 'X';
end
