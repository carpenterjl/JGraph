function out = k4(op, arg)
%K4 One probe operation in this component's session (stage K4). k4client calls it on two
%instances of one .NET component and on a second component in the same process, to measure what
%they share: globals, persistents, the base workspace, the current folder, the random stream, the
%display format, the warning state, user data, figures, and whether calls overlap.
global K4G
persistent calls
if isempty(calls)
    calls = 0;
end
calls = calls + 1;
switch op
    case 'quiet'
        out = beep;
        beep('off');
    case 'setg'
        K4G = arg;
        out = show(K4G);
    case 'getg'
        out = show(K4G);
    case 'count'
        out = show(calls);
    case 'cd'
        cd(arg);
        out = pwd;
    case 'pwd'
        out = pwd;
    case 'ctfroot'
        out = ctfroot;
    case 'deployed'
        out = sprintf('%d %d', isdeployed, ismcc);
    case 'wait'
        t = tic;
        pause(arg);
        out = sprintf('%.1f', toc(t));
    case 'fail'
        deeper(arg);
    case 'figure'
        f = figure('Visible', 'off');
        out = show(f.Number);
    case 'figures'
        out = show(numel(findall(groot, 'Type', 'figure')));
    case 'say'
        disp('k4 disp line');
        fprintf('k4 fprintf line\n');
        fprintf(2, 'k4 stderr line\n');
        out = 'said';
    case 'warn'
        lastwarn('');
        warning('k4:w', 'k4 warning');
        [m, id] = lastwarn;
        out = [id ' | ' m];
    case 'warnoff'
        warning('off', arg);
        out = 'off';
    case 'rng'
        rng(arg);
        out = 'seeded';
    case 'rand'
        out = sprintf('%.15g', rand);
    case 'format'
        format(arg);
        out = arg;
    case 'show'
        out = strtrim(evalc('disp(pi)'));
    case 'setud'
        setmcruserdata('K4', arg);
        out = 'set';
    case 'getud'
        out = show(getmcruserdata('K4'));
    case 'base'
        assignin('base', 'k4base', arg);
        out = 'assigned';
    case 'getbase'
        try
            out = show(evalin('base', 'k4base'));
        catch e
            out = ['ERR ' e.identifier];
        end
    case 'threads'
        out = show(maxNumCompThreads);
    otherwise
        error('k4:op', 'no operation %s', op);
end
end

function deeper(n)
error('k4:fail', 'k4 failure %d', n);
end

function s = show(v)
if ischar(v)
    s = v;
elseif isempty(v)
    s = ['[] ' class(v)];
elseif isnumeric(v) || islogical(v)
    s = mat2str(v);
else
    s = class(v);
end
end
