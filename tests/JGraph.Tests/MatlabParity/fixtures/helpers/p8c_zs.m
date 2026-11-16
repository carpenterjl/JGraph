function s = p8c_zs(z)
% A number as text with the sign of a zero part written out: R<x> for a real number, <re>,<im>
% for a complex one (open item 349, ADR 0386).
if isreal(z)
    s = ['R' part(real(z))];
else
    s = [part(real(z)) ',' part(imag(z))];
end
end

function s = part(x)
if x == 0
    if 1 / x < 0
        s = '-0';
    else
        s = '0';
    end
else
    s = sprintf('%.17g', x);
end
end
