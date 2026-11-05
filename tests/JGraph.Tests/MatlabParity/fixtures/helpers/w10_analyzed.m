function [out1, out2] = w10_analyzed(a, b, c)
% w10_analyzed - a function file written to raise the Code Analyzer's first set (App Designer window plan W10, ADR 0304):
% the unused family, a global and a persistent, the readability checks, a loop that grows an array, a statement no
% path reaches, and a local function nothing calls. The parity fixture w10_checkcode runs checkcode and codeIssues over it.
x = 1;
[p, q] = size(a);
out1 = q + c;
s.f = 2;
y = 1; %#ok<NASGU>
w = 1; %#ok<INUSD>
global G1
persistent P1
out2 = helper(a) + P1;
c1 = char(10);
f1 = feval('sin', 1);
e1 = exist('c1');
i1 = length(a) == 1;
d1 = datestr(1);
st = string('abc');
l1 = logical(1);
r1 = repmat(1, 2, 3);
z1 = size(a + b);
if i1 && e1
    disp(d1)
elseif i1 || e1
    disp(f1)
end
for k = 1:3
    r(k) = k;
    axes(gca);
end
disp(r)
disp(st)
disp(l1)
disp(r1)
disp(z1)
eval('disp(1)')
ans = 2;
end

function r = helper(v)
r = v;
return
disp(v)
end

function unused(a, b)
disp(b)
end
