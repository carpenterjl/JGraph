function out = ix_deadlock(which)
% IX_DEADLOCK  The cases of interop_deadlock_detected (interop plan, stage 5, ADR 0178): a function
%   handle that .NET invokes on another thread while the script waits inside a .NET call for that
%   thread. R2025b hangs for good; JGraph gives up after a timeout with JGraph:NET:DelegateDeadlock.
%   Answers the identifier of the error, 'none', or (for 'after') the value the next call answered.
p = interop_paths();
NET.addAssembly(p.assembly);
switch which
    case 'apply'
        % ApplyOnThread invokes the handle on a pool thread and blocks on the task's Result.
        try
            JGTest.Invoker.ApplyOnThread(@(x) 2 * x, 3);
            out = 'none';
        catch e
            out = e.identifier;
        end
    case 'result'
        % ApplyLater returns at once; reading Result then blocks the script inside a property get.
        t = JGTest.Invoker.ApplyLater(@(x) 2 * x, 3);
        try
            r = t.Result; %#ok<NASGU>
            out = 'none';
        catch e
            out = e.identifier;
        end
    case 'after'
        % Nothing is left waiting: the next call from another thread runs at the next pause.
        t = JGTest.Invoker.ApplyLater(@(x) 2 * x, 5);
        pause(0.2);
        out = t.Result;
    otherwise
        error('ix_deadlock: no case %s', which);
end
end
