% lb_shadow_script.m -- a script whose loop bound assigns abs into this workspace (V8).
y = zeros(1, 3);
for k = 1:lb_shadow_abs()
    y(k) = abs(k);
end
