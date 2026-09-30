function dv_vcase(v, key, pending, later, f)
% DV_VCASE  One visadev read case: clear the device and the peer, have the peer send PENDING (hex) at
%   once and LATER ('MS HEX') afterwards, then time f(v). Prints key, the answer or error, the
%   warning, the seconds taken, the VISA attributes after it, and what a run of one-byte reads
%   (Timeout 0.2) finds left over.
flush(v);
dp(v, 'reset');
if ~isempty(pending)
    dp(v, ['send ' pending]);
end
if ~isempty(later)
    dp(v, ['later ' later]);
end
pause(0.3);
lastwarn('');
t0 = tic;
try
    res = dv_describe(f(v));
catch e
    res = ['ERR ' e.identifier];
end
took = toc(t0);
[~, id] = lastwarn;
attrs = dv_vattr(v);
pause(1.8);
old = v.Timeout;
v.Timeout = 0.2;
left = [];
warning('off', 'transportlib:client:ReadWarning');
for k = 1:10
    try
        b = read(v, 1);
    catch e
        left = [left -1]; %#ok<AGROW>
        break
    end
    if isempty(b)
        break
    end
    left = [left b]; %#ok<AGROW>
end
warning('on', 'transportlib:client:ReadWarning');
v.Timeout = old;
fprintf('%s\t%s\twarn=%s\ttook=%.1f\t%s\tleft=%s\n', key, strtrim(regexprep(res, '\s*\n\s*', ' | ')), id, took, attrs, mat2str(left));
end
