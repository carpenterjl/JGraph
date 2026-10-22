function probe_k1_session()
%PROBE_K1_SESSION What the deployment builtins answer in an ordinary MATLAB session (compiler and
%SDK plan, stage K1): isdeployed, ismcc, ctfroot, mcrversion, mcrcachedir, getmcruserdata,
%setmcruserdata and waitforallfiguresclosed, with their argument refusals, and the text of the
%messages a deployed session raises. Run under matlab -batch -noFigureWindows.
beep('off');
addpath(fullfile(fileparts(mfilename('fullpath')), 'src'));

k0_try('isdeployed', @() isdeployed);
k0_try('ismcc', @() ismcc);
k0_try('isdeployed.arg', @() isdeployed(1));
k0_try('ismcc.arg', @() ismcc(1));
k0_try('ctfroot.is.matlabroot', @() strcmp(ctfroot, matlabroot));
k0_try('ctfroot.arg', @() ctfroot(1));
k0_try('mcrversion.0', @() mcrv(0));
k0_try('mcrversion.1', @() mcrv(1));
k0_try('mcrversion.2', @() mcrv(2));
k0_try('mcrversion.3', @() mcrv(3));
k0_try('mcrversion.4', @() mcrv(4));
k0_try('mcrversion.5', @() mcrv(5));
k0_try('mcrversion.arg', @() mcrversion(1));
k0_try('mcrcachedir.tail', @() tail(mcrcachedir));
k0_try('mcrcachedir.under.localappdata', @() startsWith(mcrcachedir, getenv('LOCALAPPDATA'), 'IgnoreCase', true));
k0_try('mcrcachedir.arg', @() mcrcachedir(1));

k0_try('getmcruserdata.missing', @() getmcruserdata('k1nosuch'));
k0_try('getmcruserdata.noargs', @() getmcruserdata());
k0_try('getmcruserdata.two', @() getmcruserdata('a', 'b'));
k0_try('getmcruserdata.number', @() getmcruserdata(5));
k0_try('getmcruserdata.string', @() getmcruserdata("k1nosuch"));
k0_do('setmcruserdata.char', @() setmcruserdata('k1key', 'k1value'));
k0_try('getmcruserdata.char', @() getmcruserdata('k1key'));
k0_do('setmcruserdata.double', @() setmcruserdata('k1num', [1 2 3]));
k0_try('getmcruserdata.double', @() getmcruserdata('k1num'));
k0_do('setmcruserdata.struct', @() setmcruserdata('k1s', struct('a', 1)));
k0_try('getmcruserdata.struct', @() getmcruserdata('k1s'));
k0_do('setmcruserdata.replace', @() setmcruserdata('k1key', 7));
k0_try('getmcruserdata.replaced', @() getmcruserdata('k1key'));
k0_try('getmcruserdata.case', @() getmcruserdata('K1KEY'));
k0_do('setmcruserdata.empty', @() setmcruserdata('k1key', []));
k0_try('getmcruserdata.after.empty', @() getmcruserdata('k1key'));
k0_do('setmcruserdata.one', @() setmcruserdata('k1key'));
k0_do('setmcruserdata.three', @() setmcruserdata('a', 1, 2));
k0_do('setmcruserdata.numberkey', @() setmcruserdata(5, 1));
k0_do('setmcruserdata.stringkey', @() setmcruserdata("k1str", 3));
k0_try('getmcruserdata.stringkey', @() getmcruserdata('k1str'));
k0_try('setmcruserdata.out', @() setmcruserdata('k1o', 1));

k0_try('waitforallfiguresclosed.none', @() waitforallfiguresclosed());
f = figure('Visible', 'off', 'Name', 'k1hidden');
k0_try('waitforallfiguresclosed.hidden', @() waitforallfiguresclosed());
close(f);

ids = {
    {'MCR:mainFuncForNumericStandaloneApplication:COM_ERROR_INPUT_ARGUMENT_TYPE', 'hello'}
    {'MCR:mainFuncForNumericStandaloneApplication:COM_ERROR_MISSING_FUNCTION_NAME'}
    {'MATLAB:mpath:PathAlterationNotSupported'}
    {'MATLAB:services:MissingRequiredCapability'}
    {'MATLAB:mex:DEPLOYMENT_MODE_NOTSUPPORTED'}
    {'MATLAB:run:FileNotFound', 'outside_script.m'}
    {'MATLAB:rmpath:DirNotFound', 'C:\k1'}
    {'Compiler:mcrversion:MissingMCRVersionFile', 'C:\k1\mcrversion.ver'}
    };
for k = 1:numel(ids)
    c = ids{k};
    k0_try(['message.' c{1}], @() getString(message(c{:})));
end
end

function v = mcrv(n)
if n == 0
    mcrversion;
    v = ans; %#ok<NOANS>
    return;
end
c = cell(1, n);
[c{:}] = mcrversion;
v = c;
end

function s = tail(p)
[~, s] = fileparts(p);
end
