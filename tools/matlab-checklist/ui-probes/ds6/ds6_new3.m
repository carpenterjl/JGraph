% DS6: the new-app half of ds6_verify, shard 3 of 3 (each shard writes its own .out).
setenv('DS6_PART', 'new');
setenv('DS6_SHARD', '3/3');
ds6_verify
