function s = u9_text(v)
% A value as one line for the U9 fixtures: U5's text, and a datetime as its size, format, zone and
% days. No bars.
if isdatetime(v)
    tz = v.TimeZone;
    if isempty(tz), tz = '-'; end
    s = ['datetime ' mat2str(size(v)) ' fmt=' v.Format ' tz=' tz ' [' strjoin(cellstr(v)', ',') ']'];
elseif iscell(v)
    parts = cell(1, numel(v));
    for k = 1:numel(v)
        parts{k} = u9_text(v{k});
    end
    s = ['cell ' mat2str(size(v)) ' {' strjoin(parts, ', ') '}'];
else
    s = u5_text(v);
end
s = strrep(s, '|', '/');
end
