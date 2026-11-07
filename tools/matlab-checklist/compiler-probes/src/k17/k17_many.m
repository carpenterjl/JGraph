function [s, d, p] = k17_many(a, b, c)
%K17_MANY Three inputs to three outputs: their sum, difference and product (stage K17).
s = a + b + c;
d = a - b - c;
p = a .* b .* c;
end
