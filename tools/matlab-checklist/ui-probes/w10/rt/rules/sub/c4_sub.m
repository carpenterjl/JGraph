function r = c1_cyc(x)
r = 0;
if x > 1 && x < 5
    r = 1;
elseif x > 0 || x == -1
    r = 2;
end
for k = 1:x
    r = r + k;
end
while r > 100
    r = r - 1;
end
switch r
    case 1
        r = 2;
    case {2, 3}
        r = 3;
    otherwise
        r = 4;
end
try
    r = r + 1;
catch
    r = 0;
end
end

function helper2(a)
if a, disp(1), end
end
