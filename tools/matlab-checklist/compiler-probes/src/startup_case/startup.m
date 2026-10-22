% STARTUP for k0_startup: marks that it ran and prints a line.
setappdata(groot, 'k0_startup_ran', true);
fprintf('startup.m ran (isdeployed=%d)\n', isdeployed);
