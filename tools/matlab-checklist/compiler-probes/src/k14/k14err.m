function r = k14err(kind)
%K14ERR Fail in the way asked for.
switch kind
    case 'plain', error('A plain error.');
    case 'id', error('k14:bad', 'An error with an identifier: %d.', 7);
    case 'nested', k14inner();
    case 'cause'
        ME = MException('k14:outer', 'The outer error.');
        ME = addCause(ME, MException('k14:inner', 'The cause.'));
        throw(ME);
    case 'builtin', r = [1 2] * [3 4];
    case 'undefined', r = k14nosuchfunction(1);
    case 'multiline', error('k14:lines', 'First line.\nSecond line.');
    case 'quote', error('k14:quote', 'A "quoted" \ word.');
    case 'none', r = 1;
    otherwise, r = kind;
end
end

function k14inner()
error('k14:deep', 'Raised two calls down.');
end
