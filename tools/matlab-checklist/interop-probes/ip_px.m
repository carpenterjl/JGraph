function ip_px(key, stmt)
% IP_PX  Run stmt in the caller and print key<TAB>everything it displayed, or the error it raised.
%   Newlines in the output print as " | ", and a displayed hyperlink is reduced to its text.
try
    out = evalin('caller', ['evalc(' ip_quote(stmt) ')']);
    out = regexprep(out, '<a [^>]*>([^<]*)</a>', '$1');
    out = ['OUT ' out];
catch e
    out = ['ERR ' e.identifier ' ' e.message];
end
fprintf('%s\t%s\n', key, strtrim(regexprep(out, '\s*\n\s*', ' | ')));
end

function q = ip_quote(s)
q = ['''' strrep(s, '''', '''''') ''''];
end
