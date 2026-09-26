function s = ix_try(f)
% IX_TRY  Call f with one output. Return ix_show of the answer, or ERR:<identifier> when it throws.
try
    v = f();
    s = ix_show(v);
catch e
    s = ['ERR:' e.identifier];
end
end
