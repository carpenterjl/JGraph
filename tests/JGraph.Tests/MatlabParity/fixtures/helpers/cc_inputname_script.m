% inputname called from a script (V9, ADR 0170): logs what it answers, or the refusal.
try
    q = inputname(1);
    vlog(['[' q ']']);
catch e
    vlog(['ERR ' e.message]);
end
