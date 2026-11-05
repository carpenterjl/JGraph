% a2_patterns: a script of patterns
x = 1; a = 2; b = 3; s = 'abc'; st.f = 1; v = [1 2 3];
c1 = char(10);
c2 = ['a' char(10) 'b'];
c3 = char(13);
f1 = feval('sin', 1);
f2 = feval(@sin, 1);
fn = 'sin'; f3 = feval(fn, 1);
e1 = exist('x');
e2 = exist('x', 'var');
i1 = length(v) == 1;
i2 = 1 == length(v);
i3 = length(v) ~= 1;
i4 = numel(v) == 1;
d1 = datestr(now);
d2 = datenum(2020, 1, 1);
d3 = date;
h1 = hist(v);
hist(v)
q1 = quad(@sin, 0, 1);
q2 = quadl(@sin, 0, 1);
q3 = quadv(@sin, 0, 1);
q4 = dblquad(@(x, y) x + y, 0, 1, 0, 1);
q5 = triplequad(@(x, y, z) x + y + z, 0, 1, 0, 1, 0, 1);
compass(1, 2)
in1 = inline('x+1');
fc = fcnchk('x+1');
n1 = str2num('5');
n2 = str2num(s);
s1 = string('abc');
s2 = string(s);
g1 = getfield(st, 'f');
st = setfield(st, 'f', 2);
m1 = ndims(v) == 2;
m2 = length(size(v)) == 2;
l1 = logical(1);
l2 = logical(0);
r1 = repmat(1, 2, 3);
r2 = repmat(true, 2, 3);
r3 = repmat(0, 2, 3);
r4 = repmat(false, 2, 3);
t1 = strcmp(upper(s), upper('ABC'));
t2 = strcmp(lower(s), 'abc');
t3 = strcmpi(upper(s), 'ABC');
k1 = [1];
k2 = [x];
[k3] = size(v);
k4 = [x, a];
k5 = [x,];
k6 = 1,;
k7 = 2,
for (ii = 1:3)
    disp(ii)
end
clear
clear all
clear variables
clear x
load
load a.mat
load('a.mat')
dd = load('a.mat');
eval('x = 1;')
eval(['x = ' '1;'])
eval(['load ' s])
eval(s)
z1 = size(a + b);
z2 = size(a .* b);
z3 = numel(a + b);
z4 = size(a, 1) + b;
ans = 1;
y1 = ans;
global GS
global GS
[1 2]
{1}
v(2);
x;
x + 1;
subsasgn(st, substruct('.', 'f'), 3);
ls1 = sum(v > 1);
disp(sprintf('%d', x))
nw = now;
if x == 1, disp(1), end
if (x == 1) disp(2), end
while (x < 0) x = x + 1; end
fprintf('%s\n', num2str(x));
cc = cellfun(@(c) c, {1}, 'UniformOutput', 0);
xx = x';
yy = isempty(v) == 0;
ss = strrep(s, 'a', 'b');
switch x
    case 1
        disp(1)
    case 1
        disp(2)
end
