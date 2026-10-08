function out = u10_do(fn)
% Calls fn for what it does, asking for nothing back, and answers 'ok': a u9b_chk line about a
% statement that returns nothing.
fn();
out = 'ok';
end
