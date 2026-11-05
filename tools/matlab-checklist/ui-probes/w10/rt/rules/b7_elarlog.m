function b7_elarlog(a, b)
z1 = length(a + b);
z2 = ndims(a - b);
z3 = size(a * b);
z4 = size(a / b);
z5 = numel(a .^ b);
z6 = size(a == b);
z7 = size(a & b);
z8 = size(-a);
z9 = size(a');
z10 = size(a + b, 1);
z11 = isempty(a + b);
z12 = size([a b]);
disp({z1 z2 z3 z4 z5 z6 z7 z8 z9 z10 z11 z12})
end
