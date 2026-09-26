function y = tr_bump()
% TR_BUMP  Adds 2 to the global gv and answers 1 (temporary_reuse.m): an operand that writes the
% other operand's variable while it runs, so `gv + tr_bump()` says which value of gv the sum read.
global gv;
gv = gv + 2;
y = 1;
end
