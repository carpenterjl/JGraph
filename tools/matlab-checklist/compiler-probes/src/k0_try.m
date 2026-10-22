function k0_try(key, f)
%K0_TRY Print key<TAB>value of f(), or ERR identifier | message, then any warning f raised.
lastwarn('', '');
try
    v = f();
    fprintf('%s\t%s\n', key, k0_show(v));
catch ME
    fprintf('%s\tERR %s | %s\n', key, ME.identifier, k0_flat(ME.message));
end
k0_warned(key);
end
