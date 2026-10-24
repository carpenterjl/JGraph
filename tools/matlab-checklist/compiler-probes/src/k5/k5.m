function out = k5(op, arg)
%K5 One probe operation for stage K5 (compiler and SDK plan). k5client calls it from .NET to
%measure whether the runtime delivers a component's events while the client is between calls:
%a timer started by one call ticks on (or not) while the client sleeps, and each tick prints a line,
%so where the ticks land among the client's own lines shows when they ran. No window is shown:
%the figures are invisible.
global K5T
switch op
    case 'quiet'
        out = beep;
        beep('off');
    case 'timer'
        K5T = 0;
        t = timer('Period', 0.1, 'ExecutionMode', 'fixedRate', 'TasksToExecute', arg, ...
            'TimerFcn', @tick, 'StopFcn', @(~, ~) fprintf('k5 timer stopped\n'));
        start(t);
        out = 'started';
    case 'ticks'
        out = mat2str(K5T);
    case 'wait'
        t = tic;
        pause(arg);
        out = sprintf('%.1f', toc(t));
    case 'spin'
        % A second of arithmetic with no drain point: whether a tick lands inside it.
        t = tic;
        x = 0;
        while toc(t) < arg
            x = x + 1;
        end
        out = 'spun';
    case 'figure'
        f = figure('Visible', 'off', 'Name', 'k5 invisible');
        out = mat2str(f.Number);
    case 'figures'
        out = mat2str(numel(findall(groot, 'Type', 'figure')));
    case 'closeall'
        close all force;
        out = 'closed';
    case 'deleteall'
        delete(findall(groot, 'Type', 'figure'));
        out = 'deleted';
    otherwise
        error('k5:op', 'no operation %s', op);
end
end

function tick(~, ~)
global K5T
K5T = K5T + 1;
fprintf('k5 tick %d\n', K5T);
end
