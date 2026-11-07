function y = k17_err(kind)
%K17_ERR Raises an error of the kind named, or warns, or answers 0 (stage K17).
y = 0;
switch kind
    case 'id'
        error('k17:custom', 'Custom %s with an identifier', 'text');
    case 'plain'
        error('A plain message with no identifier');
    case 'multi'
        error('k17:multi', 'Line one\nLine two');
    case 'nonascii'
        error('k17:nonascii', 'Caf\x00e9 at 20\x00b0 \x2014 na\x00efve');
    case 'nested'
        y = k17_err_inner(kind);
    case 'warn'
        warning('k17:warned', 'A warning, then 1');
        y = 1;
    case 'nargs'
        y = k17_one(1, 2);
    case 'undefined'
        y = no_such_function_k17(1);
    case 'index'
        v = [1 2 3];
        y = v(5);
end
end

function y = k17_err_inner(kind)
error('k17:nested', 'Raised two frames down for %s', kind);
end
