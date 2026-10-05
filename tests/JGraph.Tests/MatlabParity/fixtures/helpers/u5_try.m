function u5_try(label, h, name, value)
% Sets one property and prints one CHK line: what it reads back as, or the identifier and the
% sentence of the refusal, with the identifier of any warning after it (U5 fixtures).
lastwarn('');
try
    set(h, name, value);
    r = u5_text(get(h, name));
catch err
    r = [err.identifier ' :: ' u2_text(err.message)];
end
[~, wid] = lastwarn;
if ~isempty(wid)
    r = [r ' WARN ' wid];
end
fprintf('CHK|%s|%s|exact\n', label, r);
end
