function k10err(kind)
%K10ERR Raise the error asked for, for the C client to see how it arrives.
switch kind
    case 'id'
        error('k10:probe', 'A probe error with a number %d.', 3);
    case 'noid'
        error('A probe error without an identifier.');
    case 'lines'
        error('k10:lines', 'First line.\nSecond line.');
    case 'nested'
        inner();
    case 'cause'
        e = MException('k10:outer', 'The outer error.');
        e = addCause(e, MException('k10:inner', 'The inner cause.'));
        throw(e);
    case 'undefined'
        k10_no_such_function(1);
    case 'index'
        x = [1 2 3];
        disp(x(5));
    otherwise
        error('k10:kind', 'No kind %s.', kind);
end
end

function inner()
error('k10:inner', 'Raised in a local function.');
end
