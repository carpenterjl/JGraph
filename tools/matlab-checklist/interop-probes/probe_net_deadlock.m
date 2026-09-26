% probe_net_deadlock: run alone with a short timeout; it may never return.
dotnetenv("core", Version="8");
a = ip_assets();
NET.addAssembly(a.assembly);
fprintf('start\t%s\n', datestr(now, 'HH:MM:SS'));
% ---- the deadlock case: MATLAB is blocked in .NET waiting for a delegate on another thread
t0 = tic;
ip_pr('del.cross.thread', 'JGTest.Invoker.ApplyOnThread(@(x) x + 1, 1)');
fprintf('del.cross.thread.seconds\t%.1f\n', toc(t0));
