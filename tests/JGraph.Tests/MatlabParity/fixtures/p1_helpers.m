% p1_helpers.m -- the harness's way of running a fixture, proven before fixtures lean on it: the
% fixture runs by its real path (mfilename is its own name), the fixtures folder is the current
% folder (the recorder's cd, the harness's working directory), and fixtures\helpers\ is on the
% function path on both engines (the recorder's addpath, the harness's search folder), so a class
% and a function file that fixtures share are found there without living beside the fixtures --
% where both enumerations would take them for fixtures needing recordings of their own.

chk('mfilename', mfilename);
[~, tail] = fileparts(pwd);
chk('pwd_tail', tail);
chk('helper_function', sprintf('%d', helper_twice(4)));
b = HelperBox(3);
chk('helper_class', class(b));
chk('helper_property', sprintf('%d', b.p));
chk('helper_method', sprintf('%d', b.doubled()));
chk('helper_default', sprintf('%d', HelperBox().p));

function chk(name, v)
fprintf('CHK|%s|%s|exact\n', name, v);
end
