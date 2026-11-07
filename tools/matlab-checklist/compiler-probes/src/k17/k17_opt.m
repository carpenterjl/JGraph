function r = k17_opt(a, b, c)
%K17_OPT Says how many arguments arrived and what each is, for the missing-argument rules (stage K17).
parts = cell(1, nargin);
names = {'a', 'b', 'c'};
for k = 1:nargin
    switch k
        case 1, v = a;
        case 2, v = b;
        case 3, v = c;
    end
    parts{k} = sprintf('%s=%s', names{k}, k17_show(v));
end
r = sprintf('nargin=%d %s', nargin, strjoin(parts, ' '));
end
