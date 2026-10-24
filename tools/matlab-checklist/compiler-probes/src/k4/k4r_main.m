function k4r_main(varargin)
%K4R_MAIN Built once per -R option (stage K4): prints what the session sees of the option and
%writes to every stream, so a -logfile shows what it takes; given 'err', it ends on an uncaught
%error.
k0_quiet;
fprintf('main\tstarted %d\n', nargin);
k0_try('maxNumCompThreads', @() maxNumCompThreads);
k0_try('maxNumCompThreads.set4', @() maxNumCompThreads(4));
k0_try('maxNumCompThreads.after', @() maxNumCompThreads);
k0_try('usejava.jvm', @() usejava('jvm'));
disp('k4r disp line');
x = 5
fprintf(2, 'k4r stderr line\n');
warning('k4:r', 'k4r warning');
if nargin > 0 && strcmp(varargin{1}, 'err')
    error('k4:boom', 'k4r uncaught');
end
fprintf('main\tdone\n');
end
