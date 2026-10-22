function k0_warned(key)
%K0_WARNED Print the warning the last probe raised, if any.
[msg, id] = lastwarn;
if ~isempty(msg) || ~isempty(id)
    fprintf('%s.warn\t%s | %s\n', key, id, k0_flat(msg));
end
lastwarn('', '');
end
