function y = k11sub(x)
%K11SUB A file with a local function, which the library does not export: the input less one.
y = helper(x);
end

function z = helper(x)
z = x - 1;
end
