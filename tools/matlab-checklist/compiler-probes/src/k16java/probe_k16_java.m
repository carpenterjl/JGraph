% probe_k16_java: MathWorks' Java client library of MATLAB Production Server (R2025b's
% toolbox/compiler_sdk/mps_clients/java/mps_client.jar) driven from MATLAB's own JVM, since no Java
% compiler is installed here (compiler and SDK plan, stage K16). Its REST classes, MATLABParams and
% MATLABResult, marshal the request and the response; MATLAB's HTTP classes carry the bytes. The
% server's URL, the component and the jar come from K16_URL, K16_COMPONENT and K16_JAR. One line per
% case, label<TAB>text, printed to standard output; record-k16.ps1 keeps the lines with a tab.
url = getenv('K16_URL');
component = getenv('K16_COMPONENT');
javaaddpath(getenv('K16_JAR'));
import matlab.net.http.*
import matlab.net.http.field.*

function line(label, text)
    text = regexprep(text, '\(request id=[0-9:]+\)', '(request id=<N>)');
    out = '';
    for c = text
        if c == newline
            out = [out '\n']; %#ok<AGROW>
        elseif c == char(13)
            out = [out '\r']; %#ok<AGROW>
        elseif c == char(9) || (c >= 32 && c < 127)
            out = [out c]; %#ok<AGROW>
        else
            out = [out sprintf('\\u%04x', double(c))]; %#ok<AGROW>
        end
    end
    fprintf('%s\t%s\n', label, out);
end

function text = show(v)
    % A Java value the client returned, in words.
    if isa(v, 'java.lang.Object')
        cls = char(v.getClass().getName());
        if v.getClass().isArray()
            n = java.lang.reflect.Array.getLength(v);
            parts = cell(1, n);
            for i = 1:n
                parts{i} = show(java.lang.reflect.Array.get(v, int32(i - 1)));
            end
            text = sprintf('%s[%d] {%s}', cls, n, strjoin(parts, '; '));
        else
            text = sprintf('%s %s', cls, char(v.toString()));
        end
    elseif ischar(v)
        text = sprintf('char ''%s''', v);
    elseif islogical(v)
        text = sprintf('logical %s', mat2str(v));
    elseif isnumeric(v)
        text = sprintf('%s %s', class(v), mat2str(v, 17));
    elseif iscell(v)
        text = ['cell {' strjoin(cellfun(@show, v, 'UniformOutput', false), '; ') '}'];
    elseif isempty(v)
        text = 'empty';
    else
        text = sprintf('<%s>', class(v));
    end
end

function text = err(ex)
    if isa(ex, 'matlab.exception.JavaException')
        j = ex.ExceptionObject;
        cls = char(j.getClass().getName());
        if isa(j, 'com.mathworks.mps.client.MATLABException')
            frames = {};
            st = j.getMATLABStackTrace();
            for i = 1:numel(st)
                f = st(i);
                [~, name, ext] = fileparts(char(f.getFile()));
                frames{end + 1} = sprintf('%s@<F>/%s%s:%d', char(f.getName()), name, ext, f.getLine()); %#ok<AGROW>
            end
            text = sprintf('MATLABException id=%s message=%s stack=[%s]', char(j.getMATLABIdentifier()), char(j.getMessage()), strjoin(frames, ';'));
        else
            text = sprintf('EXC %s %s', cls, char(j.getMessage()));
        end
    else
        text = sprintf('EXC %s %s', ex.identifier, ex.message);
    end
end

function [status, body, headers] = post(url, component, fn, bytes)
    % The bytes as they are: MessageBody's Payload, not its Data, which MATLAB would convert by the type.
    payload = matlab.net.http.MessageBody;
    payload.Payload = uint8(bytes(:)');
    request = matlab.net.http.RequestMessage('POST', ...
        [matlab.net.http.HeaderField('Content-Type', 'application/x-google-protobuf')], payload);
    options = matlab.net.http.HTTPOptions('ConvertResponse', false, 'ConnectTimeout', 120);
    response = request.send(sprintf('%s/%s/%s', url, component, fn), options);
    status = sprintf('%d %s', double(response.StatusCode), char(response.StatusLine.ReasonPhrase));
    % The raw bytes: the Payload when MATLAB kept it, else the Data (uint8 with ConvertResponse off).
    body = response.Body.Payload;
    if isempty(body)
        body = response.Body.Data;
    end
    if ischar(body) || isstring(body)
        body = uint8(char(body));
    end
    if isempty(body)
        body = uint8([]);
    end
    body = uint8(body(:)');
    headers = response.Header;
end

function args = javaInputs(inputs)
    % Several inputs go as a Java Object[] of boxed values (MATLAB matches one input to the varargs
    % by itself, several not); a matrix among several is left to MATLAB's own conversion.
    if numel(inputs) == 1
        args = inputs;
        return
    end
    args = {javaArray('java.lang.Object', numel(inputs))};
    for i = 1:numel(inputs)
        v = inputs{i};
        if isa(v, 'java.lang.Object')
            args{1}(i) = v;
        elseif ischar(v)
            args{1}(i) = java.lang.String(v);
        elseif islogical(v) && isscalar(v)
            args{1}(i) = java.lang.Boolean(v);
        elseif isnumeric(v) && isscalar(v)
            args{1}(i) = java.lang.Double(double(v));
        else
            args = inputs;
            return
        end
    end
end

function call(label, url, component, fn, nargout, cls, varargin)
    try
        args = javaInputs(varargin);
        params = com.mathworks.mps.client.rest.MATLABParams.newInstance(int32(nargout), cls, args{:});
        sent = typecast(params.getRequestBody(), 'uint8');
        [status, body, headers] = post(url, component, fn, sent);
        shown = '';
        try
            result = com.mathworks.mps.client.rest.MATLABResult.newInstance(params, int8(typecast(body, 'int8')));
            shown = show(result.getResult());
        catch ex
            shown = err(ex);
        end
        hexed = sprintf('%02x', body);
        if numel(body) > 1500
            hexed = sprintf('<%d bytes>', numel(body));
        end
        if contains(char(body), '.m')
            hexed = '<error bytes>';
        end
        type = '';
        for h = headers
            if strcmpi(char(h.Name), 'Content-Type'), type = char(h.Value); end
        end
        line(label, sprintf('%s\tsent=%s\ttype=%s hex=%s\t%s', status, sprintf('%02x', sent), type, hexed, shown));
    catch ex
        line(label, err(ex));
    end
end

D = java.lang.Class.forName('java.lang.Double');
DD = java.lang.Class.forName('[[D');
D1 = java.lang.Class.forName('[D');
S = java.lang.Class.forName('java.lang.String');
B = java.lang.Class.forName('java.lang.Boolean');
I = java.lang.Class.forName('java.lang.Integer');
O = java.lang.Class.forName('java.lang.Object');
OO = java.lang.Class.forName('[Ljava.lang.Object;');
call('basic', url, component, 'k14add', 1, D, 1, 2);
call('basic.int', url, component, 'k14add', 1, I, 1, 2);
call('basic.object', url, component, 'k14add', 1, O, 1, 2);
call('matrix', url, component, 'k14make', 1, DD, 'matrix');
call('row', url, component, 'k14make', 1, D1, 'row');
call('row.object', url, component, 'k14make', 1, O, 'row');
call('char', url, component, 'k14make', 1, S, 'char');
call('unicode', url, component, 'k14make', 1, S, 'unicode');
call('logical', url, component, 'k14make', 1, B, 'logical');
call('cell', url, component, 'k14make', 1, OO, 'cell');
call('struct.object', url, component, 'k14make', 1, O, 'struct');
call('int8s.object', url, component, 'k14make', 1, O, 'int8s');
call('complex.object', url, component, 'k14make', 1, O, 'complex');
call('string.object', url, component, 'k14make', 1, O, 'string');
call('sparse.object', url, component, 'k14make', 1, O, 'sparse');
call('enum.object', url, component, 'k14make', 1, O, 'enum');
call('empty.object', url, component, 'k14make', 1, O, 'empty');
call('nd.object', url, component, 'k14make', 1, O, 'nd');
call('in.matrix', url, component, 'k14describe', 1, S, [1 2; 3 4]);
call('in.row', url, component, 'k14describe', 1, S, [1 2 3]);
call('in.char', url, component, 'k14describe', 1, S, 'abc');
call('in.unicode', url, component, 'k14describe', 1, S, char([955 8364 65]));
call('in.logical', url, component, 'k14describe', 1, S, true);
call('in.int32', url, component, 'k14describe', 1, S, int32(-5));
call('in.int8', url, component, 'k14describe', 1, S, int8([-1 2]));
call('in.single', url, component, 'k14describe', 1, S, single(1.5));
call('in.string', url, component, 'k14describe', 1, S, java.lang.String('jstr'));
call('in.boolean', url, component, 'k14describe', 1, S, java.lang.Boolean(true));
call('in.integer', url, component, 'k14describe', 1, S, java.lang.Integer(7));
call('in.long', url, component, 'k14describe', 1, S, java.lang.Long(9007199254740993));
call('in.cell', url, component, 'k14describe', 1, S, {1, 'a', true});
call('in.empty', url, component, 'k14describe', 1, S, []);
call('in.objects', url, component, 'k14describe', 1, S, javaArray('java.lang.Object', 2));
call('err.id', url, component, 'k14err', 1, D, 'id');
call('err.nested', url, component, 'k14err', 1, D, 'nested');
call('err.too.many.in', url, component, 'k14add', 1, D, 1, 2, 3);
call('nargout.0', url, component, 'k14add', 0, D, 1, 2);
call('nargout.2', url, component, 'k14add', 2, D, 1, 2);
call('multi.3', url, component, 'k14multi', 3, O, 2);
call('missing.function', url, component, 'nosuch', 1, D, 1);
call('missing.component', url, 'nosuch', 'k14add', 1, D, 1, 2);
