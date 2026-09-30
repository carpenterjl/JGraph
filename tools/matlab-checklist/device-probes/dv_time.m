function t = dv_time(f)
% DV_TIME  Seconds f() takes, to a tenth, its answer dropped as soon as it is made.
t0 = tic;
x = f(); %#ok<NASGU>
t = round(toc(t0), 1);
clear x
end
