% serial_timing.m -- how long the device classes' round trips take (device classes plan, stage D13,
% ADR 0197). Runs unchanged in R2025b and in JGraph, on the com0com pair COM20<->COM21 with both ends
% held by this one script, and on the loopback for TCP. It prints one line per measurement,
% "name<TAB>value<TAB>unit", so two runs are compared line by line.
%
%   matlab -batch "run('tools/devices/timing/serial_timing.m')"
%   jgraph.exe -batch tools/devices/timing/serial_timing.m
%
% There is no gate. A row more than three times slower than R2025b goes to the open-items file.
a = serialport("COM20", 115200, Timeout=30);
b = serialport("COM21", 115200, Timeout=30);
configureTerminator(a, "LF");
configureTerminator(b, "LF");

% Warm up: the first calls pay for loading and compiling.
for k = 1:50
    writeline(a, "warm");
    readline(b);
    write(a, 1, "uint8");
    read(b, 1, "uint8");
end

n = 1000;

t = tic;
for k = 1:n
    writeline(a, "hello world");
    r = readline(b);
end
fprintf('writeline+readline\t%.1f\tus\n', toc(t) / n * 1e6);

t = tic;
for k = 1:n
    write(a, 65, "uint8");
    r = read(b, 1, "uint8");
end
fprintf('write+read 1 byte\t%.1f\tus\n', toc(t) / n * 1e6);

t = tic;
for k = 1:n
    writeline(a, "*IDN?");
    q = readline(b);
    writeline(b, "JGraph,peer,0,1");
    r = readline(a);
end
fprintf('query and answer\t%.1f\tus\n', toc(t) / n * 1e6);

t = tic;
for k = 1:n
    write(a, 1:64, "double");
    r = read(b, 64, "double");
end
fprintf('write+read 64 doubles\t%.1f\tus\n', toc(t) / n * 1e6);

t = tic;
for k = 1:n
    x = a.NumBytesAvailable;
end
fprintf('NumBytesAvailable\t%.2f\tus\n', toc(t) / n * 1e6);

data = uint8(mod(0:1048575, 251));
t = tic;
write(a, data, "uint8");
got = read(b, numel(data), "uint8");
fprintf('1 MiB write+read\t%.2f\tMB/s\n', numel(data) / toc(t) / 1e6);
assert(isequal(uint8(got), data));

t = tic;
writebinblock(a, data, "uint8");
got = readbinblock(b, "uint8");
fprintf('1 MiB binblock\t%.2f\tMB/s\n', numel(data) / toc(t) / 1e6);
assert(isequal(uint8(got), data));

lines = 2000;
t = tic;
for k = 1:lines
    writeline(a, "0123456789012345678901234567890123456789");
end
for k = 1:lines
    r = readline(b);
end
fprintf('2000 queued lines\t%.1f\tus/line\n', toc(t) / lines * 1e6);

t = tic;
for k = 1:20
    clear s
    s = serialportlist;
end
fprintf('serialportlist\t%.2f\tms\n', toc(t) / 20 * 1e3);

clear a b
t = tic;
for k = 1:20
    a = serialport("COM20", 115200);
    clear a
end
fprintf('open and close\t%.2f\tms\n', toc(t) / 20 * 1e3);

% TCP on the loopback, through the echo server.
u0 = udpport;
port = u0.LocalPort;
clear u0
echotcpip("on", port);
c = tcpclient("127.0.0.1", port, Timeout=30);
configureTerminator(c, "LF");
for k = 1:50
    writeline(c, "warm");
    readline(c);
end
t = tic;
for k = 1:n
    writeline(c, "hello world");
    r = readline(c);
end
fprintf('tcp writeline+readline\t%.1f\tus\n', toc(t) / n * 1e6);
t = tic;
write(c, data, "uint8");
got = read(c, numel(data), "uint8");
fprintf('tcp 1 MiB echo\t%.2f\tMB/s\n', numel(data) / toc(t) / 1e6);
assert(isequal(uint8(got), data));
clear c
echotcpip("off");
