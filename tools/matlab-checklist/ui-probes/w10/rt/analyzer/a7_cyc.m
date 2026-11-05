function r = a7_cyc(x)
if x > 1
    r = 1;
elseif x > 0
    r = 2;
else
    r = 3;
end
for k = 1:x
    r = r + k;
end
end

function helper(a)
disp(a)
end
