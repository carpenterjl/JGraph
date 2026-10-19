% DS2 (designer plan): what R2025b's appdesigner says before it opens anything, for JGraph's
% appdesigner. Headless, and never a call that would open App Designer: the capability it needs, its
% file name validator (appdesigner.internal.application.getValidatedInputAppFileName) on each kind of
% name, and appdesigner itself only where the validator refuses first.
import matlab.internal.capability.Capability;
here = pwd;
rt = fullfile(here, 'rt'); if ~exist(rt, 'dir'), mkdir(rt); end
copyfile(fullfile(matlabroot, 'toolbox', 'matlab', 'licensing', '+matlab', '+internal', '+licensing', 'borrowLicenses.mlapp'), ...
    fullfile(rt, 'BorrowCopy.mlapp'));
fid = fopen(fullfile(rt, 'NotAnApp.txt'), 'w'); fclose(fid);

fprintf('WebWindow supported: %d\n', Capability.isSupported(Capability.WebWindow));
try
    Capability.require(Capability.WebWindow);
    fprintf('require: passes\n');
catch e
    fprintf('require: %s | %s\n', e.identifier, e.message);
end

cd(rt);
cases = {5, ["a" "b"], 'for', '1abc', 'NoSuchApp', 'NoSuchApp.mlapp', 'NotAnApp.txt', 'BorrowCopy.MLAPP', ...
    'BorrowCopy', 'BorrowCopy.mlapp', 'borrowcopy.mlapp', fullfile(rt, 'BorrowCopy'), "BorrowCopy.mlapp", 'BorrowCopy.m'};
for k = 1:numel(cases)
    c = cases{k};
    try
        v = appdesigner.internal.application.getValidatedInputAppFileName(c);
        fprintf('%-24s -> %s\n', disp_(c), strrep(v, rt, '<rt>'));
    catch e
        fprintf('%-24s !! %s | %s\n', disp_(c), e.identifier, strrep(e.message, rt, '<rt>'));
    end
end

% appdesigner itself, only where its validator refuses before any window: its arity and outputs.
try, appdesigner('NoSuchApp'); catch e, fprintf('appdesigner(NoSuchApp) !! %s | %s\n', e.identifier, strrep(e.message, rt, '<rt>')); end
try, appdesigner('NoSuchApp', 2); catch e, fprintf('appdesigner(2 args) !! %s | %s\n', e.identifier, e.message); end
try, x = appdesigner('NoSuchApp'); catch e, fprintf('x = appdesigner !! %s | %s\n', e.identifier, strrep(e.message, rt, '<rt>')); end
fprintf('nargin %d nargout %d\n', nargin('appdesigner'), nargout('appdesigner'));
cd(here);

function s = disp_(c)
    if ischar(c), s = ['''' c '''']; elseif isstring(c), s = ['"' char(join(c, '","')) '"']; else, s = mat2str(c); end
end
