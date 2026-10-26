function response = k15webbad(request)
%K15WEBBAD A web request handler answering in the shape the path's last segment asks for.
segments = strsplit(request.Path, '/');
kind = segments{end};
q = strfind(kind, '?');
if ~isempty(q)
    kind = kind(1:q(1) - 1);
end
ok = struct('ApiVersion', [1 0 0], 'HttpCode', 200, 'HttpMessage', 'OK', ...
    'Headers', {{'Content-Type', 'text/plain'}}, 'Body', uint8(['kind=' kind]));
switch kind
    case 'ok', response = ok;
    case 'nocode', response = rmfield(ok, 'HttpCode');
    case 'nomessage', response = rmfield(ok, 'HttpMessage');
    case 'noapi', response = rmfield(ok, 'ApiVersion');
    case 'noheaders', response = rmfield(ok, 'Headers');
    case 'nobody', response = rmfield(ok, 'Body');
    case 'empty', response = struct();
    case 'number', response = 5;
    case 'text', response = 'not a struct';
    case 'bodychar', response = ok; response.Body = 'a char body';
    case 'bodydouble', response = ok; response.Body = [72 105];
    case 'bodyempty', response = ok; response.Body = uint8([]);
    case 'bodycolumn', response = ok; response.Body = uint8('col')';
    case 'code404', response = ok; response.HttpCode = 404; response.HttpMessage = 'Gone fishing';
    case 'code201', response = ok; response.HttpCode = 201; response.HttpMessage = 'Created';
    case 'code599', response = ok; response.HttpCode = 599; response.HttpMessage = 'Odd';
    case 'codeint', response = ok; response.HttpCode = int32(202);
    case 'codetext', response = ok; response.HttpCode = '200';
    case 'codefraction', response = ok; response.HttpCode = 200.5;
    case 'messageempty', response = ok; response.HttpMessage = '';
    case 'messagestring', response = ok; response.HttpMessage = "Fine";
    case 'headersrow', response = ok; response.Headers = {'Content-Type', 'text/plain', 'X-A', '1'};
    case 'headersnumber', response = ok; response.Headers = {'X-Num', 42};
    case 'headersempty', response = ok; response.Headers = {};
    case 'headerslength', response = ok; response.Headers = {'Content-Length', '3'};
    case 'headersmany', response = ok; response.Headers = {'X-A', 'a'; 'X-B', 'b'; 'Set-Cookie', 'c=1'};
    case 'headersstruct', response = ok; response.Headers = struct('a', 1);
    case 'apiother', response = ok; response.ApiVersion = [2 0 0];
    case 'apirow', response = ok; response.ApiVersion = [1 0];
    case 'extra', response = ok; response.Extra = 1;
    case 'array', response = [ok ok];
    case 'error', error('k15:web', 'The handler failed on purpose.');
    case 'slow', pause(3); response = ok;
    otherwise, response = ok; response.Body = uint8(['unknown kind ' kind]);
end
end
