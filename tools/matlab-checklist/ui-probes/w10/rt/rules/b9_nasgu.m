function out = b9_nasgu(c, f)
x = 1;
if c
    x = 2;
end
disp(x)
for k = 1:3
end
disp(k)
[a, b] = f();
disp(b)
[~, b2] = f();
disp(b2)
p(1).f = 2;
q.f(2) = 3;
cl{1} = 2;
n = 0;
for k2 = 1:3
    n = n + 1;
end
out = 1;
out = 2;
m1 = 1; m1 = m1 + 1;
persistent pp
if isempty(pp), pp = 0; end
pp = pp + 1;
global gg
gg = 1;
persistent p2
persistent p3
p3 = 1;
w = 5;
nested();
    function nested()
        disp(w)
    end
end

function b9_varg(varargin)
end

function b9_nargin(a, b)
if nargin > 1, disp(1), end
end

function b9_tilde(~, b)
disp(b)
end

function r = b9_nodef()
for i = 1:3
    if i > 1, disp(prev), end
    prev = i;
end
r = i;
end

function b9_unrch(c)
if c
    error('x:y', 'boom');
    disp(1)
end
for k = 1:3
    continue
    disp(k)
end
while true
    disp(1)
end
disp(2)
end

function x = b9_agrow(n)
x = [];
for k = 1:n
    x(k) = k;
    x = [x; k];
    x{end+1} = k;
    x(end+1, :) = [1 2];
    x(end+1).f = 1;
    y(k) = k;
end
z = zeros(1, n);
for k = 1:n
    z(k) = k;
    z(end+1) = k;
end
disp(z); disp(y)
end

function [r, s] = b9_stout(c)
if c
    r = 1;
end
end

function varargout = b9_varout()
end

function b9_effects(x, s, c)
x.f;
c{1};
s(1).a;
x(1);
sin(x);
x';
-x;
x == 1;
x(1) + 1;
1:3;
[1 2];
{1};
@sin;
x
[1 2]
x;
end

function b9_ok(a) %#ok<FOO>
x = 1; %#ok<*FOO>
y = 2; %#ok<NASGU,FOO>
z = 3; %#ok<NASGU> %#ok<INUSD>
disp(a)
end

function b9_loops(h)
for k = 1:3
    figure(h);
    hold on
    plot(k)
    axes(h)
    while k > 5
        axes(h);
    end
end
axes(h);
end

function b9_misc(v, s, x)
disp(sprintf('%d', x));
disp(sprintf('%d\n', x))
c = char(10);
d = [char(10)];
e = exist(v);
g = exist('v', 'var', 3);
f = isempty(v) == 0;
n = str2num(num2str(1));
l = length(s) == 1;
r = repmat(1, 3);
r2 = repmat(1, [2 3]);
r3 = repmat(2, 2, 3);
r4 = repmat('a', 2, 3);
q = quad('sin', 0, 1);
k = logical(1);
k2 = logical(2);
k3 = logical([1 0]);
m = ndims(v) == 2;
m2 = 2 == ndims(v);
st = string('a');
st2 = string('a', 'b');
st3 = string("a");
dt = datestr(1);
dt2 = x.datestr;
dt3 = x.datestr(1);
hh = @hist;
gf = getfield(x, 'a', {1});
sf = setfield(x, 'a', 1);
disp({c d e g f n l r r2 r3 r4 q k k2 k3 m m2 st st2 st3 dt dt2 dt3 hh gf sf})
end
