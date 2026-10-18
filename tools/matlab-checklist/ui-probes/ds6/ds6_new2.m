% DS6: the new-app half of ds6_verify, shard 2 of 3 (each shard writes its own .out).
setenv('DS6_PART', 'new');
setenv('DS6_SHARD', '2/3');
ds6_verify
