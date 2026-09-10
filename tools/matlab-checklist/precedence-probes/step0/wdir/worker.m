% worker.m: a script run by name from probe0, in its own folder with its own private/.
worker_pf = pf();
worker_pfp = pfp();
worker_helper = helper(1);
worker_phelp = phelp();

function r = helper(x)
    r = sprintf('worker-helper(%d)', x);
end
