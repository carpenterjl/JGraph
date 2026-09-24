function n = lb_shadow_abs()
% lb_shadow_abs.m -- a loop bound that assigns the builtin's name abs into its caller's workspace (V8).
assignin('caller', 'abs', [4 5 6]);
n = 3;
end
