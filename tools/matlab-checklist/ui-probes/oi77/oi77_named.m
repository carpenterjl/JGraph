function oi77_named(~, ~, n)
% OI77_NAMED A callback by name for the oi77 probes.
if nargin < 3
    disp('named callback');
else
    fprintf('named callback %d\n', n);
end
end
