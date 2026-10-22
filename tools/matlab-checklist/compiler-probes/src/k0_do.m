function k0_do(key, f)
%K0_DO Run f() for its effect; print key<TAB>ok or ERR identifier | message, then any warning.
lastwarn('', '');
try
    f();
    fprintf('%s\tok\n', key);
catch ME
    fprintf('%s\tERR %s | %s\n', key, ME.identifier, k0_flat(ME.message));
end
k0_warned(key);
end
