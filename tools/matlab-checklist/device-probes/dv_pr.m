function dv_pr(key, expr)
% DV_PR  Evaluate expr in the caller and print key<TAB>what it returned, or key<TAB>ERR id message.
try
    v = evalin('caller', expr);
    out = dv_describe(v);
catch e
    out = ['ERR ' e.identifier ' ' e.message];
end
fprintf('%s\t%s\n', key, strtrim(regexprep(out, '\s*\n\s*', ' | ')));
end
