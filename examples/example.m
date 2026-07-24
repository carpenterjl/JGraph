% A MATLAB script, written the way MATLAB writes them, running unmodified in JGraph.
%
% Nothing here is JGraph-specific: % comments, 1-based indexing, no 'let', '...' continuations,
% transpose, cells, structs, a switch, try/catch, an anonymous function, and a function definition
% at the bottom of the file.

%% Measurement -----------------------------------------------------------------------------------
fs = 500;                          % sample rate, Hz
t  = (0:1/fs:1-1/fs)';             % a column of sample times
signal = 2.0 * sin(2*pi*7*t) + ...
         0.5 * sin(2*pi*40*t) + ...
         0.1 * randn(size(t));

%% Summary ---------------------------------------------------------------------------------------
stats.count = numel(signal);
stats.peak  = max(abs(signal));
stats.rms   = sqrt(mean(signal.^2));

[largest, at] = max(signal);
fprintf('%d samples, peak %.3f, rms %.3f\n', stats.count, stats.peak, stats.rms);
fprintf('largest value %.3f at sample %d (t = %.3f s)\n', largest, at, t(at));

%% Classify ------------------------------------------------------------------------------------
labels = {'quiet', 'nominal', 'loud'};
switch grade(stats.rms)
    case 1
        name = labels{1};
    case 2
        name = labels{2};
    otherwise
        name = labels{3};
end
fprintf('this run is %s\n', name);

%% A handle, applied over a cell ------------------------------------------------------------------
scale = @(x) x / stats.peak;
windows = {signal(1:100), signal(101:200), signal(201:300)};
levels = cellfun(@(w) max(abs(scale(w))), windows);
disp(levels)

%% Guarded work ----------------------------------------------------------------------------------
try
    disp(signal(99999));
catch err
    fprintf('recovered: %s\n', err.message);
end

%% Plot -------------------------------------------------------------------------------------------
plot(t, signal);
hold on
plot(t, scale(signal) * stats.peak, 'r--');
title('measurement');
xlabel('time (s)');
ylabel('amplitude');
legend('signal', 'scaled');
grid on
show();

%% Helper ------------------------------------------------------------------------------------------
function level = grade(rms)
if rms < 0.5
    level = 1;
elseif rms < 2
    level = 2;
else
    level = 3;
end
end
