function k0_exit(mode)
%K0_EXIT End a standalone the way its one argument names; the runner records the exit code.
k0_quiet;
if nargin < 1, mode = 'return'; end
fprintf('mode\t%s\n', mode);
switch mode
    case 'return'
        x = 5 %#ok<NOPRT> the echo of an unterminated statement
    case 'error'
        error('k0:boom', 'Boom %d happened', 7);
    case 'nested'
        k0_exit_inner(3);
    case 'builtin'
        ones(2) * ones(3);
    case 'cause'
        ME = MException('k0:outer', 'Outer failure');
        ME = addCause(ME, MException('k0:inner', 'Inner reason'));
        throw(ME);
    case 'exit3'
        exit(3);
    case 'quit4'
        quit(4);
    case 'quitforce'
        quit(5, 'force');
    case 'warning'
        warning('k0:careful', 'Careful with %s', 'that');
    case 'disp'
        s.a = 1; s.b = 'two';
        s %#ok<NOPRT>
        c = {1, 'x'} %#ok<NOPRT>
        disp(pi);
    otherwise
        fprintf('unknown mode\n');
end
fprintf('after\t%s\n', mode);
end

function k0_exit_inner(n)
if n > 0
    k0_exit_inner(n - 1);
else
    error('k0:deep', 'Deep error at depth zero');
end
end
