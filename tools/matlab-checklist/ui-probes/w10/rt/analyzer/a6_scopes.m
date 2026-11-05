function r = a6_scopes(a, b, varargin)
if nargin < 2
    b = 1;
end
f = @(t) t + a;
r = f(b) + nested1();
for k = 1:3
    disp(1)
end
try
    c = 1;
catch err
    c = 2;
end
disp(c)
ev = 1;
eval('disp(ev)')
hold on
shared = 5;
    function n = nested1()
        n = shared;
    end
end

function varargout = a6_local(varargin)
varargout{1} = numel(varargin);
end
