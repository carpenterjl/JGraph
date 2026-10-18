% DS0 window session (run with the user's leave, 2026-10-18): opens App Designer with no app in
% R2025b's full desktop, so ds0w_dock.ps1 can list and capture the windows and see whether App
% Designer is its own window or part of the desktop. Nothing is created or saved; MATLAB exits
% by itself after the capture window.
fprintf('webui feature: %d\n', feature('webui'));
fprintf('desktop in use: %d\n', usejava('desktop') || feature('webui') == 1);
appdesigner;
fprintf('appdesigner returned at %s\n', char(datetime('now', 'Format', 'HH:mm:ss')));
pause(75);
fprintf('exiting at %s\n', char(datetime('now', 'Format', 'HH:mm:ss')));
exit
