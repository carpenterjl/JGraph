function f = maker()
v = 'maker-v';
f = @(x) g(x, v);
end

function r = helper()
r = 'maker-helper';
end
