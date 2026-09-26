function y = tr_scaled(A, i)
% TR_SCALED  A scaled by i/1000 (temporary_reuse.m): a call on the right of an update, so the
% update's left operand is held while the call runs.
y = A * (i * 1e-3);
end
