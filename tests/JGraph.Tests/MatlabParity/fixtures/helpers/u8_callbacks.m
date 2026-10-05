function s = u8_callbacks(h, name)
% What a callback property makes of each form a callback can be given in (U8 fixtures).
values = {@sin, 'disp(1)', {@sin, 1}, "str", 5, {5}, []};
parts = cell(1, numel(values));
for k = 1:numel(values)
    try
        set(h, name, values{k});
        parts{k} = u5_text(get(h, name));
    catch err
        parts{k} = err.identifier;
    end
end
s = strjoin(parts, ' ; ');
end
