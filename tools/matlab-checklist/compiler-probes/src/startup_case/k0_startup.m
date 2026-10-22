function k0_startup(varargin)
%K0_STARTUP Built from a folder holding a startup.m; reports whether that startup ran.
k0_quiet;
k0_try('k0_startup_ran', @() getappdata(groot, 'k0_startup_ran'));
k0_try('exist.startup', @() exist('startup')); %#ok<EXIST>
k0_try('which.startup', @() which('startup'));
k0_try('isdeployed', @() isdeployed);
end
