function s = dc_loop(n)
% A MATLAB loop the loop compiler takes (V11): it compiles under the function's own dialect,
% whoever called the function.
s = 0;
for i = 1:n
    s = s + i;
end
end
