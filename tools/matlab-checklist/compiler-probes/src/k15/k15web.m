function response = k15web(request)
%K15WEB A web request handler: answers with a JSON description of the request it was given.
fields = fieldnames(request);
parts = cell(1, numel(fields));
for k = 1:numel(fields)
    v = request.(fields{k});
    parts{k} = sprintf('"%s":{"class":"%s","size":[%s]}', fields{k}, class(v), strjoin(arrayfun(@(d) sprintf('%d', d), size(v), 'UniformOutput', false), ','));
end
headers = request.Headers;
hdr = cell(1, size(headers, 1));
for k = 1:size(headers, 1)
    value = headers{k, 2};
    if isnumeric(value)
        value = sprintf('%g', value);
    end
    hdr{k} = sprintf('{"name":"%s","value":"%s"}', k15escape(headers{k, 1}), k15escape(value));
end
body = sprintf(['{"fields":{%s},"Method":"%s","Path":"%s","HttpVersion":[%s],"ApiVersion":[%s],' ...
    '"HeaderCount":%d,"Headers":[%s],"Body":"%s"}'], strjoin(parts, ','), k15escape(request.Method), k15escape(request.Path), ...
    strjoin(arrayfun(@(d) sprintf('%d', d), request.HttpVersion, 'UniformOutput', false), ','), ...
    strjoin(arrayfun(@(d) sprintf('%d', d), request.ApiVersion, 'UniformOutput', false), ','), ...
    size(headers, 1), strjoin(hdr, ','), k15escape(char(request.Body)));
response = struct('ApiVersion', [1 0 0], 'HttpCode', 200, 'HttpMessage', 'OK', ...
    'Headers', {{'Content-Type', 'application/json'; 'X-K15', 'handled'}}, 'Body', uint8(body));
end

function s = k15escape(s)
s = strrep(strrep(strrep(char(s), '\', '\\'), '"', '\"'), newline, '\n');
end
