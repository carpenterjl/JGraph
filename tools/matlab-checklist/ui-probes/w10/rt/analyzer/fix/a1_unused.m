function [out1, out2, out3] = a1_unused(a, ~, c, d)
% the unused family
x = 1;
[~, q] = size(a);
out1 = q + c;
s.f = 2;
t = struct();
t.g = 3;
out2 = helper1(d);
disp(out2)
y = 1; %#ok<NASGU>
z = 1; %#ok
w = 1; 
out3 = helper2(1);
u1 = 1;
u1 = 2;
disp(u1)
v1(2) = 3;
helper6(3);
helper7(gca);
helper4();
helper5(1);
end

function r = helper1(v)
global G2
persistent P1
r = v + G2 + P1;
end

function r = helper2(v, ~)
disp(v)
end

function helper3(~, b)
disp(b)
end

function r = helper4()
if rand > 0.5
    m = 1;
end
r = m;
r = r + k;
k = 2;
end

function r = helper5(v)
r = v;
return
disp(v)
end

function r = helper6(n)
r = [];
for k = 1:n
    r(end+1) = k;
    r = [r k];
    if k > 2
        break
        disp(k)
    end
end
end

function helper7(h)
for k = 1:3
    axes(h);
    plot(k)
end
end
