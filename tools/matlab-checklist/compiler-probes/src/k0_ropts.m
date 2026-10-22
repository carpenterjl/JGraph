function k0_ropts(varargin)
%K0_ROPTS Built with -R options (-startmsg, -completemsg, -logfile, -singleCompThread); reports
%what the session sees of them.
k0_quiet;
fprintf('main\tstarted\n');
k0_try('maxNumCompThreads', @() maxNumCompThreads);
k0_try('usejava.jvm', @() usejava('jvm'));
disp('k0_ropts disp line');
fprintf('main\tdone\n');
end
