function r = p8c_nth(fn, k, n)
% The k-th of n outputs of fn() (open items 346 and 349, ADR 0386).
outs = cell(1, n);
[outs{:}] = fn();
r = outs{k};
end
