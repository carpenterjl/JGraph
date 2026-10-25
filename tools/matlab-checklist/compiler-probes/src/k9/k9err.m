function r = k9err(kind)
%K9ERR Fail in the way asked for.
switch kind
    case 'plain', error('A plain error.');
    case 'id', error('k9:bad', 'An error with an identifier: %d.', 7);
    case 'nested', k9inner();
    case 'cause'
        ME = MException('k9:outer', 'The outer error.');
        ME = addCause(ME, MException('k9:inner', 'The cause.'));
        throw(ME);
    case 'builtin', r = [1 2] * [3 4];
    case 'undefined', r = k9nosuchfunction(1);
    case 'none', r = 1;
end
end

function k9inner()
error('k9:deep', 'Raised two calls down.');
end
