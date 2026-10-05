function s = u8_data(t)
% A table's data on one line: its class and size, and what its names and column flags then read
% as (U8 fixtures).
d = get(t, 'Data');
v = get(t, 'DisplayData');
s = sprintf('%s %s display %s %s names %s rows %s', class(d), mat2str(size(d)), class(v), mat2str(size(v)), ...
    u5_text(get(t, 'ColumnName')), u5_text(get(t, 'RowName')));
end
