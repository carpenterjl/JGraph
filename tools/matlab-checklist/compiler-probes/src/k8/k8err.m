function k8err(kind)
%K8ERR Raise the error asked for, for the client to see how it arrives.
switch kind
    case 'id'
        error('k8:probe', 'A probe error with a number %d.', 3);
    case 'noid'
        error('A probe error without an identifier.');
    case 'lines'
        error('k8:lines', 'First line.\nSecond line.');
    case 'nested'
        inner();
    case 'undefined'
        k8_no_such_function(1);
    case 'cause'
        e = MException('k8:outer', 'The outer error.');
        e = addCause(e, MException('k8:inner', 'The inner cause.'));
        throw(e);
    case 'index'
        x = [1 2 3];
        disp(x(5));
    otherwise
        error('k8:kind', 'No kind %s.', kind);
end
end

function inner()
error('k8:inner', 'Raised in a local function.');
end
